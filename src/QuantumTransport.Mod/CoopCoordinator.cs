using Il2CppMoonCleaners;
using Il2CppSteamworks;
using QuantumTransport.Core;

namespace QuantumTransport;

internal sealed class CoopCoordinator(QuantumMod mod)
{
    internal TransportRole Role { get; private set; }
    internal string LocalPlayer { get; private set; } = "";
    internal CoopAdvertisement? HostAdvertisement { get; private set; }
    private MoonWorld? _world;
    private string _session=Guid.NewGuid().ToString("N"), _lastValue="";
    private long _sequence, _lastPublish, _lastRead, _lastHostChange, _nextAttempt, _lastSync;
    private bool _wasHost,_wasClient;
    private int _seededBlock=-1;
    private readonly CapsuleSources _consumed=new();
    private readonly CapsuleSources _accepted=new("qt.handoff.v1:");
    private readonly HashSet<int> _outgoing=[];
    private readonly Dictionary<int,(byte[] Bytes,long LastSent)> _pending=[];
    internal int InFlight => _outgoing.Count;
    internal int PendingPackets => _pending.Count;
    internal long RejectedPackets {get;private set;}
    internal long SendFailures {get;private set;}
    internal string? LastRejection {get;private set;}
    private long _lastRejectionLog;
    internal long StagedObjects { get; private set; }
    internal long AcceptedObjects { get; private set; }
    internal long DuplicatePackets { get; private set; }

    private void Reject(string reason)
    {
        RejectedPackets++; LastRejection=reason;
        if(Environment.TickCount64-_lastRejectionLog<2000)return;
        _lastRejectionLog=Environment.TickCount64;
        mod.Diagnostics.Write(DiagnosticLevel.Info,"handoff_deferred","交接暂未接收；客户端保留同编号资源等待重试。",new{reason});
    }

    internal void Update(MoonWorld world)
    {
        if (_world != world)
        {
            _world=world; _session=Guid.NewGuid().ToString("N"); _sequence=0;
            _lastPublish=0; _lastRead=0; _lastValue=""; _wasHost=false; _wasClient=false;
            _outgoing.Clear(); _pending.Clear(); _accepted.Reset();
            HostAdvertisement=null; _seededBlock=-1; _consumed.Reset();
        }
        bool joined=MoonCrew.Joined, inLobby=MoonSteamLobby.InLobby, host=MoonSteamLobby.IsHost;
        int members=inLobby ? (int)MoonSteamLobby.Members.Count : 0;
        if (!joined && host && members>1) _wasHost=true;
        if(joined) _wasClient=true;
        var role=!joined && _wasClient ? TransportRole.Waiting : !joined && _wasHost ? TransportRole.Host : CoopPolicy.Role(joined,inLobby,host,members);
        string local=MoonCrew.LocalPlayerId;
        if (role != Role || local != LocalPlayer)
        {
            Role=role; LocalPlayer=local; mod.Manager.ResetSources();
            mod.Diagnostics.Write(DiagnosticLevel.Info,"network_role","物流权限已更新。",new {role=Role.ToString(),members});
        }
        long now=Environment.TickCount64;
        var bound=MoonCrewSession.Instance;
        int size=MoonCrew.IdBlockSize, current=world.nextCanisterId;
        int block=joined?(int)MoonCrew.AssignedIdBlock:size>0?current/size*size:-1;
        if(size>0 && block>=0 && current>=block && (long)current<(long)block+size && _seededBlock!=block &&
            world.vehicleStorage!=null && (!joined || bound!=null && bound!.bound==world && !(bool)bound.Joining))
        {
            // Native UseIdBlock scans live objects only. Consumed IDs must also be
            // skipped after reconnect/load, otherwise a new capsule could reuse a receipt.
            current=_accepted.NextAvailable(world,current,block,size);
            world.nextCanisterId=_consumed.NextAvailable(world,current,block,size);
            _seededBlock=block;
        }
        if (inLobby && host && !joined && now-_lastPublish>=500)
        {
            _lastPublish=now;
            var ad=new CoopAdvertisement(MoonSteamLobby.HostId,_session,++_sequence,
                mod.Settings.Enabled && mod.Settings.EnableCoop && mod.Settings.CollectLooseCapsules,mod.Manager.DustDemand,
                (mod.Settings.CollectPlayerDust?1:0)|(mod.Settings.CollectVehicleDust?2:0));
            SteamMatchmaking.SetLobbyData(new CSteamID(MoonSteamLobby.LobbyId),CoopAdvertisement.Key,ad.Encode());
        }
        if (joined && inLobby && now-_lastRead>=250)
        {
            _lastRead=now;
            string value=SteamMatchmaking.GetLobbyData(new CSteamID(MoonSteamLobby.LobbyId),CoopAdvertisement.Key);
            if(value!=_lastValue) { _lastValue=value; _lastHostChange=now; }
            HostAdvertisement=CoopAdvertisement.TryParse(value,MoonCrew.HostId,out var ad) &&
                (ulong)MoonCrew.HostId==(ulong)MoonSteamLobby.HostId && now-_lastHostChange<=5000 ? ad : null;
        }
    }

    internal void Receive(MoonCrewSession session,ulong sender,byte[] bytes)
    {
        if (!CoopFrame.TryDecode(bytes,out var f) || f==null) return;
        if ((bool)MoonCrew.Joined)
        {
            if(f.Type==2 && sender==(ulong)MoonCrew.HostId && _pending.TryGetValue(f.Id,out var sent) &&
                CoopFrame.TryDecode(sent.Bytes,out var original) && original?.Session==f.Session) _pending.Remove(f.Id);
            return;
        }
        var world=session.bound;
        if(f.Type!=1 || f.Session!=_session || world==null || world!=_world || !(bool)MoonSteamLobby.IsHost) return;
        bool member=false;
        var members=MoonSteamLobby.Members;
        for(int i=0;i<(int)members.Count;i++) if((ulong)members[i].id==sender) { member=true; break; }
        if(!member || session.idBlocks==null || !(bool)session.idBlocks.ContainsKey(sender)) return;
        int block=session.idBlocks.get_Item(sender), size=MoonCrew.IdBlockSize;
        if(size<=0 || f.Id<block || (long)f.Id>=(long)block+size || f.Kilograms>(double)MoonWorld.CapsuleCapacityKg) return;
        if(_accepted.Contains(world!,f.Id)) DuplicatePackets++;
        else
        {
            if(f.VehicleStorageId.Length>0 && !mod.Manager.ControlsVehicle(world!,MoonCrew.PlayerId(sender),f.VehicleStorageId,f.Owner))
            { Reject("vehicle_identity_or_controller_not_ready");return; }
            if((bool)world!.canisters.ContainsKey(f.Id)) { Reject("native_id_already_present");return; }
            var save=world!.PendingSave;
            if(save!=null && (bool)save.IsFaulted) return;
            NativeWire.Apply(world,MoonCrew.PlayerId(sender),f);
            _accepted.Record(world,f.Id);
            world.RaiseMachineCanister(MoonWorld.CanisterOp.Upsert,world.canisters.get_Item(f.Id));
            AcceptedObjects++;
        }
        NativeWire.Send(session,sender,CoopFrame.Ack(f.Session,f.Id).Encode());
    }

    internal void TickClient(MoonWorld world)
    {
        var session=MoonCrewSession.Instance;
        var ad=HostAdvertisement;
        if(session==null || session!.bound!=world || (bool)session.Joining || (int)MoonCrew.AssignedIdBlock<=0)
        { mod.Diagnostics.SetState("client_joining"); return; }
        long now=Environment.TickCount64;
        foreach(int id in _outgoing.ToArray())
            if(!_pending.ContainsKey(id) && !(bool)world.canisters.ContainsKey(id)) _outgoing.Remove(id);
        foreach(var item in _pending.ToArray())
            if(now-item.Value.LastSent>=1000 && NativeWire.Send(session!,MoonCrew.HostId,item.Value.Bytes))
                _pending[item.Key]=(item.Value.Bytes,now);
        if(ad==null || !ad.Enabled) { mod.Diagnostics.SetState("client_waiting_for_host_mod"); return; }
        var save=world.PendingSave;
        if(save!=null && (!(bool)save.IsCompleted || (bool)save.IsFaulted)) { mod.Diagnostics.SetState("pending_save"); return; }
        if(now<_nextAttempt || ad.DustDemand<=FlowPlanner.Epsilon || _outgoing.Count>=mod.Settings.MaxClientCapsulesInFlight)
        { mod.Diagnostics.SetState("client_backpressure"); return; }
        int created=0;
        foreach(var source in mod.Manager.ClientSources(world))
        {
            if(created>=mod.Settings.ClientCapsulesPerTick || _outgoing.Count>=mod.Settings.MaxClientCapsulesInFlight) break;
            if(source.Kind is "player" or "player_container" && (ad.SourceMask&1)==0 || source.Kind=="vehicle" && (ad.SourceMask&2)==0) continue;
            if(!(source.Exists?.Invoke()??false) || source.Read()<=FlowPlanner.Epsilon) continue;
            double amount=Math.Min(source.Read(),Math.Min(ad.DustDemand,(double)MoonWorld.CapsuleCapacityKg));
            var ticket=NativeHandoff.Pack(world,session!,source,amount);
            if(ticket==null) continue;
            bool queued=false;
            try
            {
                var frame=NativeWire.Create(world,world.canisters.get_Item(ticket.Id),ad.Session,source);
                var packet=frame.Encode();
                if(!CoopFrame.TryDecode(packet,out _)) throw new InvalidOperationException("Locally generated handoff failed validation");
                queued=NativeWire.Send(session!,MoonCrew.HostId,packet);
                if(queued) { _pending[ticket.Id]=(packet,now); _outgoing.Add(ticket.Id); StagedObjects++; created++; }
            }
            finally { if(!queued) NativeHandoff.UndoUnsent(world,ticket); }
            if(!queued) { SendFailures++; _nextAttempt=now+1000; break; }
        }
        mod.Diagnostics.SetState(created>0?"client_handoff":"client_waiting_for_supply");
    }

    internal void MarkHostChanged()
    {
        if(Role!=TransportRole.Host || Environment.TickCount64-_lastSync<mod.Settings.NetworkSyncMilliseconds) return;
        var session=MoonCrewSession.Instance;
        if(session==null) return;
        _lastSync=Environment.TickCount64;
        // Let the game's normal send loop deduplicate and serialize snapshots.
        session!.nextMachineStorage=0; session.nextProduction=0; session.walletDirty=true;
    }
    internal void ClearAdvertisement()
    {
        if((bool)MoonSteamLobby.InLobby && (bool)MoonSteamLobby.IsHost)
            SteamMatchmaking.SetLobbyData(new CSteamID(MoonSteamLobby.LobbyId),CoopAdvertisement.Key,"");
    }
}
