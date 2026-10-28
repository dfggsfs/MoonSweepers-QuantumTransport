using System.Diagnostics;
using Il2CppMoonCleaners;
using QuantumTransport.Core;
using GameString=Il2CppSystem.String;

namespace QuantumTransport;

internal sealed record Receiver(int Id,int Slot,MoonProductionState State,ResourceKind Resource=ResourceKind.Dust,int Priority=0)
{ internal double Space; }
internal sealed record ResourceSource(int MachineId,ResourceKind Resource,string Kind,string Owner,
    Func<double> Read,Action<double> Write,Action? Cleanup=null,Func<bool>? Exists=null,Action? AfterCommit=null,string? StorageId=null);
internal sealed record TransportStats(int Packagers,int CompatibleReceivers,int NonemptySources,double AvailableKilograms,
    long CommittedTransfers,double TransferredKilograms,int ReceiverPorts=0,int ProductSources=0,
    double DustTransferredKilograms=0,double SandTransferredKilograms=0,double RetainedPackagerKilograms=0,
    double EligibleDustSpaceKilograms=0,int PlayerSources=0,int VehicleSources=0,int RobotSources=0,int StoneSources=0,
    double CollectedDustTransferredKilograms=0,double StoneDeliveredKilograms=0,int UnresolvedStorageSources=0,int GarageSources=0,
    double GarageDustTransferredKilograms=0);
internal sealed record SchedulerStats(int IndexedSources,int ExaminedSources,int ReceiverSamples,int Moves,
    double TickMilliseconds,double LastCatalogMilliseconds,double PeakMilliseconds,long BudgetYields,int ReceiptRanges);

internal sealed class TransportManager(QuantumMod mod)
{
    private readonly CollectionSources _collections=new();
    private readonly CapsuleSources _capsules=new();
    private readonly List<Receiver> _receivers=[];
    private readonly Dictionary<(ResourceKind,int),Receiver> _receiverMap=[];
    private readonly Dictionary<(ResourceKind,int),int> _receiverIndices=[];
    private readonly Dictionary<int,List<GameString>> _packagerKeys=[];
    private readonly Dictionary<int,string> _packagers=[];
    private PrefixIndex<int> _prefixes=new();
    private List<ResourceSource> _sources=[];
    private List<ResourceSource> _baseSources=[],_capsuleBindings=[];
    private int _signature=int.MinValue,_storageCount=-1,_capsuleCount=-1,_inventoryCount=-1,_farCount=-1,_vehicleRevision=-1,_dockHash;
    private int _sourceCursor,_probeCursor,_targetCursor,_unresolved;
    private bool _hasTopology,_busy;
    private long _nextCatalog,_lastSaveRequest,_transfers,_budgetYields;
    private double _moved,_dust,_sand,_stone,_collected,_peak,_catalogMs,_garage;
    internal TransportStats LastStats {get;private set;}=new(0,0,0,0,0,0);
    internal SchedulerStats Performance {get;private set;}=new(0,0,0,0,0,0,0,0,0);
    internal double DustDemand => _receivers.Where(r=>r.Resource==ResourceKind.Dust).Sum(r=>r.Space);
    internal bool ControlsVehicle(MoonWorld world,string player,string storage,string owner)=>_collections.ControlsVehicle(world,player,storage,owner);

    // Read-only dashboard probes are bounded independently from the hot scheduler.
    // Totals describe indexed entries; masses describe at most 256 sampled entries.
    internal DashboardData CaptureDashboard(int page)
    {
        var settings=mod.Settings;
        var groups=new Dictionary<string,(int Count,double Kg,int Sampled)>(StringComparer.Ordinal);
        string Group(ResourceSource s)=>s.Kind switch
        {
            "player" or "player_container"=>"人物 / 容器", "vehicle"=>"清扫车料斗",
            "robot" or "robot_far"=>"机器人 / 停靠站", "garage"=>"车库已卸货储仓",
            "packager"=>"打包机", "finished_product"=>"机器产出", _=>"散落 / 交接胶囊"
        };
        var inventory=_sources.Where(s=>s.Kind!="duplicate_capsule").ToArray();
        string[] groupNames=inventory.Select(Group).ToArray();
        foreach(var name in groupNames)
        {
            groups.TryGetValue(name,out var v); groups[name]=(v.Count+1,v.Kg,0);
        }
        int sampled=0;
        foreach(int index in DashboardSampling.Select(groupNames,256))
        {
            var source=inventory[index];
            if(!(source.Exists?.Invoke()??(mod.CurrentWorld!=null && (bool)mod.CurrentWorld!.depots.ContainsKey(source.MachineId)))) continue;
            double kg=source.Read(); if(!double.IsFinite(kg) || kg<0) continue;
            sampled++;
            string name=groupNames[index]; var v=groups[name]; groups[name]=(v.Count,v.Kg+kg,v.Sampled+1);
        }
        string[] names=["人物 / 容器","清扫车料斗","机器人 / 停靠站","车库已卸货储仓","打包机","机器产出","散落 / 交接胶囊"];
        var sourceRows=names.Select(name=>{groups.TryGetValue(name,out var v);return new DashboardSource(name,v.Count,v.Kg,v.Sampled);}).ToArray();
        var machines=_receivers.Where(r=>r.Resource==ResourceKind.Dust).ToArray();
        int safePage=Math.Clamp(page,0,DashboardLayout.PageCount(machines.Length)-1);
        var rows=new List<DashboardMachine>();
        foreach(var r in machines.Skip(safePage*DashboardLayout.MachinesPerPage).Take(DashboardLayout.MachinesPerPage))
        {
            var s=r.State; bool chamber=(int)s.Kind==(int)MoonWorld.PressureChamberKind;
            bool running=s.Running; double dust=s.Stored(r.Slot),capacity=s.Capacity(r.Slot);
            double sand=chamber?0:(double)s.Stored(MoonWorld.PressSandIn),sandCapacity=chamber?0:(double)s.Capacity(MoonWorld.PressSandIn);
            string status=running?"加工中":(double)s.Power<=0?"等待供电":"待机";
            rows.Add(new(r.Id,chamber?"加压舱":"压石机",status,dust,capacity,sand,sandCapacity,
                Math.Clamp((double)s.Progress,0,1),(int)s.Cycles));
        }
        var p=Performance;
        return new(mod.CurrentWorld!=null,settings.Enabled,mod.Coop.Role switch
            {TransportRole.Host=>"联机主机",TransportRole.Client=>"联机客户端",TransportRole.Waiting=>"等待联机权限",_=>"单人世界"},
            mod.Diagnostics.State,_dust,_sand,_stone,_garage,DustDemand,sampled,sourceRows.Sum(s=>s.Count),sourceRows,machines.Length,rows.ToArray(),
            p.TickMilliseconds,p.BudgetYields,mod.Coop.InFlight,mod.Coop.PendingPackets,mod.Coop.RejectedPackets,
            settings.DustDistribution.ToString(),settings.ChamberDustPriority,settings.PressDustPriority,settings.PressDustBufferKilograms,
            settings.PackagerDustReserveKilograms,settings.CollectPlayerDust,settings.CollectVehicleDust,settings.CollectRobotDust,
            settings.CollectGarageDust,settings.DeliverStone,mod.Diagnostics.LastError, !settings.Enabled);
    }

    internal void ResetSources()
    {
        _collections.Reset(); _capsules.Reset(); _sources.Clear(); _baseSources.Clear(); _capsuleBindings.Clear(); _receivers.Clear(); _receiverMap.Clear();
        _hasTopology=false; _nextCatalog=0; _sourceCursor=0; _probeCursor=0; _targetCursor=0;
        _storageCount=-1; _capsuleCount=-1; _inventoryCount=-1; _farCount=-1; _lastSaveRequest=0;
        LastStats=new(0,0,0,0,0,0); Performance=new(0,0,0,0,0,0,0,0,0);
    }

    private void Refresh(MoonWorld world,bool client)
    {
        long started=Stopwatch.GetTimestamp();
        int signature=world.StructureSignature;
        bool topology=!_hasTopology || signature!=_signature;
        if(topology)
        {
            _signature=signature; _hasTopology=true; _receivers.Clear(); _receiverMap.Clear();
            _packagers.Clear(); _prefixes=new();
            if(!client)
            {
                var e=world.depots.GetEnumerator();
                while((bool)e.MoveNext())
                {
                    var d=e.Current.Value; if(d==null) continue;
                    int id=d.id,kind=d.kind;
                    if(kind==(int)MoonWorld.PackagerKind)
                    { string prefix=MoonWorld.PackagerStoragePrefix(id); _packagers[id]=prefix; _prefixes.Add(prefix,id); continue; }
                    if(kind!=(int)MoonWorld.PressureChamberKind && kind!=(int)MoonWorld.StonePressKind) continue;
                    var s=world.ProductionOf(id); if(s==null || s.stores==null) continue;
                    int priority=kind==(int)MoonWorld.PressureChamberKind?mod.Settings.ChamberDustPriority:mod.Settings.PressDustPriority;
                    AddReceiver(s,d,ResourceKind.Dust,MoonCargo.Dust,priority);
                    AddReceiver(s,d,ResourceKind.Sand,MoonCargo.Sand,0);
                }
                _receivers.Sort((a,b)=>a.Id.CompareTo(b.Id));
                _receiverIndices.Clear();
                for(int i=0;i<_receivers.Count;i++) _receiverIndices[(_receivers[i].Resource,_receivers[i].Id)]=i;
            }
            _probeCursor=0; _targetCursor=0;
        }
        int storage=world.vehicleStorage.Count, caps=world.canisters==null?0:(int)world.canisters.Count;
        int inventories=world.inventoryMass==null?0:(int)world.inventoryMass.Count;
        int far=world.farRobots==null?0:(int)world.farRobots.Count;
        int revision=world.OwnedVehiclesRevision, dockHash=0;
        var docks=MoonRobotDock.All;
        if(!client && docks!=null)
            for(int i=0;i<(int)docks.Count;i++) { var d=docks[i]; if(d!=null && d!.World==world) dockHash=HashCode.Combine(dockHash,(int)d.DockId); }
        long now=Environment.TickCount64;
        bool rebuildBase=topology || now>=_nextCatalog || storage!=_storageCount || inventories!=_inventoryCount ||
            far!=_farCount || revision!=_vehicleRevision || dockHash!=_dockHash;
        if(!rebuildBase && caps==_capsuleCount) return;
        _storageCount=storage; _capsuleCount=caps; _inventoryCount=inventories; _farCount=far; _vehicleRevision=revision; _dockHash=dockHash;
        if(rebuildBase)
        {
        _nextCatalog=now+mod.Settings.CatalogRefreshMilliseconds;
        var sources=new List<ResourceSource>(); _packagerKeys.Clear();
        if(!client)
        {
            foreach(var receiver in _receivers.Where(r=>r.Resource==ResourceKind.Dust))
                AddProductSources(sources,receiver.State,receiver.Id,
                    (int)receiver.State.Kind==(int)MoonWorld.PressureChamberKind?(int)MoonWorld.ChamberSandOut:(int)MoonWorld.PressStoneOut,
                    (int)receiver.State.Kind==(int)MoonWorld.PressureChamberKind?ResourceKind.Sand:ResourceKind.Stone);
            var entries=world.vehicleStorage.GetEnumerator();
            while((bool)entries.MoveNext())
            {
                GameString key=entries.Current.Key;
                if(!_prefixes.TryMatch(key,out int id,out int length)) continue;
                string owner=((string)key)[length..]; if(!SourceRegistry.ValidOwner(owner)) continue;
                if(!_packagerKeys.TryGetValue(id,out var keys)) _packagerKeys[id]=keys=[];
                keys.Add(key);
                sources.Add(new(id,ResourceKind.Dust,"packager",owner,
                    ()=> (bool)world.vehicleStorage.ContainsKey(key)?(double)world.vehicleStorage.get_Item(key).kilograms:0,
                    value=>world.vehicleStorage.get_Item(key).kilograms=value,
                    Exists:()=> (bool)world.vehicleStorage.ContainsKey(key) && (bool)world.depots.ContainsKey(id)));
            }
        }
        _unresolved=_collections.Add(world,mod.Settings,sources,mod.Coop.Role,mod.Coop.LocalPlayer);
        _baseSources=sources;
        }
        // A new client capsule must not force a native rescan of every factory.
        // Rebuild only capsule bindings when that collection changes.
        _capsuleBindings=[];
        if(!client && mod.Settings.CollectLooseCapsules && world.canisters!=null) _capsules.Add(world,_capsuleBindings);
        _sources=[.._baseSources,.._capsuleBindings];
        if(_sources.Count>0) _sourceCursor%=_sources.Count; else _sourceCursor=0;
        _catalogMs=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    private void AddReceiver(MoonProductionState state,MoonDepotSnapshot depot,ResourceKind resource,MoonCargo cargo,int priority)
    {
        int slot=MoonWorld.InputSlot(depot.kind,cargo);
        if(slot<0) return;
        var receiver=new Receiver(depot.id,slot,state,resource,priority);
        _receivers.Add(receiver); _receiverMap[(resource,receiver.Id)]=receiver;
    }

    private List<ResourceSource> SelectSources()
    {
        int count=Math.Min(_sources.Count,mod.Settings.MaxSourcesPerTick);
        var result=new List<ResourceSource>(count);
        for(int i=0;i<count;i++) result.Add(_sources[(_sourceCursor+i)%_sources.Count]);
        return result;
    }
    internal IReadOnlyList<ResourceSource> ClientSources(MoonWorld world)
    {
        if(world.depots==null || world.vehicleStorage==null || world.inventoryMass==null) return [];
        Refresh(world,true); var selected=SelectSources(); _sourceCursor=ScanCursor.Advance(_sourceCursor,1,_sources.Count);return selected;
    }

    internal void Tick(MoonWorld world)
    {
        if(_busy) return;
        if(world.depots==null || world.production==null || world.vehicleStorage==null) { mod.Diagnostics.SetState("world_initializing"); return; }
        var save=world.PendingSave;
        if(save!=null && (bool)save.IsFaulted) { mod.Diagnostics.SetState("save_failed"); return; }
        if(save!=null && !(bool)save.IsCompleted) { mod.Diagnostics.SetState("pending_save"); return; }
        _busy=true;
        long start=Stopwatch.GetTimestamp(),before=_transfers;
        int samples=0,moves=0,examined=0;
        double available=0,held=0;
        bool Expired()=>Stopwatch.GetElapsedTime(start).TotalMilliseconds>=mod.Settings.TickBudgetMilliseconds || moves>=mod.Settings.MaxTransfersPerTick;
        var selected=new List<ResourceSource>();
        try
        {
            Refresh(world,false);
            for(int i=0;i<Math.Min(_receivers.Count,mod.Settings.MaxReceiverSamplesPerTick) && !Expired() &&
                Stopwatch.GetElapsedTime(start).TotalMilliseconds<mod.Settings.TickBudgetMilliseconds*0.25;i++)
            {
                var r=_receivers[_probeCursor%_receivers.Count]; _probeCursor=(_probeCursor+1)%_receivers.Count;
                r.Space=(bool)world.depots.ContainsKey(r.Id)?EffectiveSpace(r):0; samples++;
            }
            selected=SelectSources();
            bool station=StoneDelivery.HasStation(world);
            mod.Diagnostics.DeliveryState=!mod.Settings.DeliverStone?"disabled":!station?"no_station":"waiting_for_stone";
            var supply=new Dictionary<int,double>();
            var groupBudget=new Dictionary<int,double>();
            for(int i=0;i<selected.Count && !Expired() && Stopwatch.GetElapsedTime(start).TotalMilliseconds<mod.Settings.TickBudgetMilliseconds*0.5;i++)
            {
                examined=i+1;
                var source=selected[i];
                if(!(source.Exists?.Invoke()??(bool)world.depots.ContainsKey(source.MachineId))) continue;
                if(source.Kind=="duplicate_capsule") { source.AfterCommit?.Invoke(); moves++; continue; }
                double kg=source.Read();
                if(!double.IsFinite(kg) || kg<0) throw new InvalidOperationException("Invalid source quantity");
                available+=kg;
                if(kg<=FlowPlanner.Epsilon) continue;
                if(source.Resource==ResourceKind.Stone)
                {
                    if(!mod.Settings.DeliverStone || !station) continue;
                    var delivery=StoneDelivery.Apply(world,source);
                    mod.Diagnostics.DeliveryState=delivery.Kilograms>0?"delivering":"station_full";
                    if(delivery.Kilograms>0)
                    {
                        source.AfterCommit?.Invoke(); _transfers++; moves++; _moved+=delivery.Kilograms; _stone+=delivery.Kilograms;
                        mod.Diagnostics.Transfer(new {sourceId=source.MachineId,sourceKind=source.Kind,cargo="Stone",destination="station",delivery.Kilograms});
                    }
                    continue;
                }
                if(source.Kind=="packager" && mod.Settings.PackagerDustReserveKilograms>0)
                {
                    if(!groupBudget.TryGetValue(source.MachineId,out double budget))
                    {
                        double total=_packagerKeys[source.MachineId].Sum(k=>(bool)world.vehicleStorage.ContainsKey(k)?(double)world.vehicleStorage.get_Item(k).kilograms:0);
                        held+=Math.Min(total,mod.Settings.PackagerDustReserveKilograms);
                        budget=FeedPolicy.TransferableSupply(total,mod.Settings.PackagerDustReserveKilograms);
                    }
                    kg=Math.Min(kg,budget); groupBudget[source.MachineId]=budget-kg;
                }
                supply[i]=kg;
            }
            foreach(var resource in new[]{ResourceKind.Sand,ResourceKind.Dust})
            {
                if(Expired()) break;
                var offers=supply.Where(p=>p.Value>FlowPlanner.Epsilon && selected[p.Key].Resource==resource)
                    .Select(p=>new SourceOffer(p.Key,selected[p.Key].MachineId,p.Value)).ToArray();
                if(offers.Length==0) continue;
                var ports=new List<ReceiverPort>();
                for(int n=0;n<_receivers.Count;n++)
                {
                    var r=_receivers[(_targetCursor+n)%_receivers.Count];
                    if(r.Resource==resource) ports.Add(new(r.Id,resource,r.Space,r.Priority));
                }
                var plan=BatchRouter.Plan(resource,offers,ports,mod.Settings.MaxTransfersPerTick-moves);
                int last=-1;
                foreach(var item in plan)
                {
                    if(Expired()) break;
                    var source=selected[item.SourceIndex];
                    var receiver=_receiverMap[(resource,item.ReceiverId)];
                    if(!(bool)world.depots.ContainsKey(receiver.Id) || !(source.Exists?.Invoke()??(bool)world.depots.ContainsKey(source.MachineId))) continue;
                    double free=EffectiveSpace(receiver);
                    receiver.Space=free;
                    double amount=Math.Min(item.Kilograms,Math.Min(source.Read(),free));
                    if(amount<=FlowPlanner.Epsilon) continue;
                    var commit=CommitOne(source,receiver,amount);
                    receiver.Space=Math.Max(0,free-commit.Kilograms);
                    receiver.State.lastInput=world.productionNow; world.progressDirty=true;
                    _transfers++; moves++; _moved+=commit.Kilograms;
                    if(resource==ResourceKind.Dust) _dust+=commit.Kilograms; else _sand+=commit.Kilograms;
                    if(source.Kind is "player" or "player_container" or "vehicle" or "robot" or "robot_far" or "garage" or "network_capsule") _collected+=resource==ResourceKind.Dust?commit.Kilograms:0;
                    if(source.Kind=="garage") _garage+=commit.Kilograms;
                    source.AfterCommit?.Invoke(); source.Cleanup?.Invoke();
                    if(resource==ResourceKind.Sand && _receiverMap.TryGetValue((ResourceKind.Dust,receiver.Id),out var dust)) dust.Space=EffectiveSpace(dust);
                    last=_receiverIndices[(resource,receiver.Id)];
                    if(source.Kind=="packager") world.packagerStatus.set_Item(source.MachineId,(GameString)"量子传送中");
                    mod.Diagnostics.Transfer(new {sourceId=source.MachineId,sourceKind=source.Kind,receiverId=receiver.Id,cargo=resource.ToString(),receiver.Priority,commit.Kilograms});
                }
                if(last>=0 && _receivers.Count>0) _targetCursor=(last+1)%_receivers.Count;
            }
            if(_transfers!=before && Environment.TickCount64-_lastSaveRequest>5000)
            { _lastSaveRequest=Environment.TickCount64; world.RequestSave(); }
            mod.Diagnostics.SetState(_transfers!=before?"transferring":Expired()?"budget_yield":available<=FlowPlanner.Epsilon?"waiting_for_supply":
                _receivers.Count==0?"no_compatible_receivers":DustDemand<=FlowPlanner.Epsilon?"demand_satisfied":"waiting_for_receiver_refresh");
        }
        finally
        {
            _sourceCursor=ScanCursor.Advance(_sourceCursor,examined,_sources.Count);
            double ms=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            _peak=Math.Max(_peak,ms); if(Expired()) _budgetYields++;
            LastStats=new(_packagers.Count,_receivers.Select(r=>r.Id).Distinct().Count(),selected.Count,available,_transfers,_moved,
                _receivers.Count,_sources.Count(s=>s.Kind=="finished_product"),_dust,_sand,held,DustDemand,
                _sources.Count(s=>s.Kind is "player" or "player_container"),_sources.Count(s=>s.Kind=="vehicle"),
                _sources.Count(s=>s.Kind is "robot" or "robot_far"),_sources.Count(s=>s.Resource==ResourceKind.Stone),_collected,_stone,_unresolved,
                _sources.Count(s=>s.Kind=="garage"),_garage);
            Performance=new(_sources.Count,examined,samples,moves,ms,_catalogMs,_peak,_budgetYields,_capsules.ReceiptRanges);
            _busy=false;
            if(_transfers!=before) mod.Coop.MarkHostChanged();
        }
    }

    private static double PhysicalSpace(Receiver r)=>Math.Max(0,(double)r.State.Capacity(r.Slot)-(double)r.State.Stored(r.Slot));
    private double EffectiveSpace(Receiver r)=>r.Resource!=ResourceKind.Dust || (int)r.State.Kind!=(int)MoonWorld.StonePressKind ? PhysicalSpace(r) :
        FeedPolicy.PressDustSpace(r.State.Stored(r.Slot),r.State.Capacity(r.Slot),r.State.Stored(MoonWorld.PressSandIn),mod.Settings.PressDustBufferKilograms,mod.Settings.DustDistribution);

    private static void AddProductSources(List<ResourceSource> sources,MoonProductionState state,int id,int slot,ResourceKind resource)
    {
        var store=state.stores[slot]; if(store==null) return;
        var e=store.GetEnumerator();
        while((bool)e.MoveNext())
        {
            GameString key=e.Current.Key; string owner=key;
            if(!SourceRegistry.ValidOwner(owner)) continue;
            sources.Add(new(id,resource,"finished_product",owner,
                ()=>(bool)store.ContainsKey(key)?(double)store.get_Item(key):0,value=>store.set_Item(key,value),
                ()=>{if((bool)store.ContainsKey(key) && (double)store.get_Item(key)<=FlowPlanner.Epsilon) store.Remove(key);}));
        }
    }

    private static CommittedTransfer CommitOne(ResourceSource source,Receiver receiver,double amount)
    {
        var target=receiver.State.stores[receiver.Slot];
        if(target==null) throw new InvalidOperationException("Processor input store is missing");
        GameString owner=source.Owner; bool had=target.ContainsKey(owner);
        double before=had?(double)target.get_Item(owner):0;
        return TransferCommit.Apply(amount,source.Read,source.Write,
            ()=>(bool)target.ContainsKey(owner)?(double)target.get_Item(owner):0,
            value=>MoonWorld.Put(target,owner,value),
            ()=>{if(had) target.set_Item(owner,before); else target.Remove(owner);});
    }

    internal static double TransferPackager(MoonVehicleStorageSnapshot source,string owner,IReadOnlyList<Receiver> receivers,
        Func<int,bool> stillExists,Action<Receiver,CommittedTransfer>? committed=null)=>
        TransferSource(new(-1,ResourceKind.Dust,"packager",owner,()=>(double)source.kilograms,v=>source.kilograms=v),receivers,stillExists,committed);

    internal static double TransferSource(ResourceSource source,IReadOnlyList<Receiver> receivers,Func<int,bool> stillExists,
        Action<Receiver,CommittedTransfer>? committed=null,Func<Receiver,double>? demandSpace=null,double sourceLimit=double.MaxValue)
    {
        double supply=source.Read();
        if(!double.IsFinite(supply)||supply<0||!double.IsFinite(sourceLimit)||sourceLimit<0||!SourceRegistry.ValidOwner(source.Owner))
            throw new InvalidOperationException("Invalid source inventory");
        supply=Math.Min(supply,sourceLimit); if(supply<=FlowPlanner.Epsilon) return 0;
        double Free(Receiver r)=>Math.Min(PhysicalSpace(r),demandSpace?.Invoke(r)??PhysicalSpace(r));
        var plan=ResourceRouter.Allocate(source.Resource,source.MachineId,supply,receivers.Select(r=>new ReceiverPort(r.Id,r.Resource,stillExists(r.Id)?Free(r):0,r.Priority)).ToArray());
        var map=receivers.ToDictionary(r=>(r.Resource,r.Id)); double moved=0;
        foreach(var p in plan)
        {
            var r=map[(source.Resource,p.ReceiverId)]; if(!stillExists(r.Id)) continue;
            double amount=Math.Min(supply-moved,Math.Min(source.Read(),Math.Min(Free(r),p.Kilograms)));
            if(amount<=FlowPlanner.Epsilon) continue;
            var result=CommitOne(source,r,amount); committed?.Invoke(r,result); moved+=amount;
        }
        source.Cleanup?.Invoke(); return moved;
    }
}
