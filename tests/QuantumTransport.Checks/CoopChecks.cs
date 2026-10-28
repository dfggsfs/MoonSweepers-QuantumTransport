using QuantumTransport.Core;
using System.Diagnostics;
using System.Text.Json;

internal static class CoopChecks
{
    internal static void Run(Action<bool,string> check,Action<double,double,string> near,Action<Action,string> reject)
    {
        var random=new Random(50027);
        check(CoopPolicy.Role(true,true,false,2)==TransportRole.Client,"joined peer is client");
        check(CoopPolicy.Role(false,true,true,2)==TransportRole.Host,"lobby owner is authority");
        check(CoopPolicy.Role(false,true,false,2)==TransportRole.Waiting,"incomplete join never runs factory");
        foreach(string kind in new[]{"player","player_container","vehicle","robot","packager","finished_product"})
        {
            check(CoopPolicy.MayRead(TransportRole.SinglePlayer,kind,"remote","remote","local"),"solo retains adapters");
            check(!CoopPolicy.MayRead(TransportRole.Waiting,kind,"remote","remote","local"),"waiting cannot debit");
            check(CoopPolicy.MayRead(TransportRole.Host,kind,"remote","remote","local")==!(kind is "player" or "player_container" or "vehicle"),"host cannot debit client-authoritative mirror");
            check(CoopPolicy.MayRead(TransportRole.Client,kind,"local","local","local")== (kind is "player" or "player_container" or "vehicle"),"client debits only locally controlled sources");
        }
        check(CoopPolicy.MayRead(TransportRole.Host,"vehicle","remote","","local"),"host owns parked shared vehicle");
        check(CoopPolicy.MayRead(TransportRole.Client,"vehicle","original-owner","local","local"),"borrowed vehicle keeps contributor but follows actual driver authority");
        string session=Guid.NewGuid().ToString("N");
        var ad=new CoopAdvertisement(123,session,7,true,23.25,3);
        check(CoopAdvertisement.TryParse(ad.Encode(),123,out var decodedAd)&&decodedAd==ad,"capability roundtrip");
        foreach(string invalid in new[]{"",ad.Encode().Replace("1|123|","2|123|"),ad.Encode().Replace("23.25","NaN"),ad.Encode().Replace("|3","|4"),new string('a',257)})
            check(!CoopAdvertisement.TryParse(invalid,123,out _),"invalid capability refused");
        check(!CoopAdvertisement.TryParse(ad.Encode(),124,out _),"stale host capability refused");
        var frame=new CoopFrame(1,session,10_000_001,"original-owner",5,20,[1,2,3],[0,1,0],[0,0,0,1],false,[1,2,3]);
        check(CoopFrame.TryDecode(frame.Encode(),out var decoded)&&decoded?.Id==frame.Id&&decoded.Owner==frame.Owner&&decoded.PlayerState.SequenceEqual(frame.PlayerState),"atomic source/cargo frame roundtrip");
        check(CoopFrame.TryDecode(CoopFrame.Ack(session,frame.Id).Encode(),out var ack)&&ack?.Type==2,"ack roundtrip");
        var starter=frame with {VehicleStorageId="starter:original-owner",VehicleKilograms=15,VehicleCapacity=100};
        check(CoopFrame.TryDecode(starter.Encode(),out var vehicle)&&vehicle?.VehicleKilograms==15&&vehicle.VehicleStorageId==starter.VehicleStorageId,"starter/borrowed vehicle source snapshot included atomically");
        check(!CoopFrame.TryDecode((starter with {VehicleKilograms=101}).Encode(),out _),"vehicle final stock cannot exceed reported capacity");
        check(!CoopFrame.TryDecode((frame with {VehicleKilograms=5}).Encode(),out _),"vehicle balance requires an explicit source key");
        foreach(var invalid in new[]{frame with {Id=0},frame with {Session="old"},frame with {Kilograms=-1},frame with {Capacity=4},frame with {Up=[0,0,0]},frame with {Rotation=[]},frame with {PlayerState=[]},frame with {PlayerState=new byte[32769]}})
            check(!CoopFrame.TryDecode(invalid.Encode(),out _),"invalid frame refused");
        check(!CoopFrame.TryDecode([0x51,0x54,0x48,0x35,123],out _),"truncated JSON refused");
        check(!CoopFrame.TryDecode(new byte[CoopFrame.MaxBytes+1],out _),"oversize frame refused");
        for(int i=0;i<frame.Encode().Length;i++) check(!CoopFrame.TryDecode(frame.Encode()[..i],out _),"truncated handoff cannot create cargo");

        var ranges=new IdRanges(); var reference=new HashSet<int>();
        for(int i=0;i<10000;i++)
        {
            int id=random.Next(1,20000); ranges.Add(id); reference.Add(id);
            int probe=random.Next(1,20000);
            check(ranges.Contains(probe)==reference.Contains(probe),"random exact receipt membership");
        }
        for(int i=1;i<20000;i++) check(ranges.Contains(i)==reference.Contains(i),"no false receipt holes");
        var restored=new IdRanges(); foreach(var r in ranges.Ranges) restored.AddRange(r.First,r.Last);
        check(restored.Ranges.SequenceEqual(ranges.Ranges),"save/load preserves exact receipt ranges");
        ranges=new(); ranges.Add(10); ranges.Add(12); ranges.Add(11);
        check(ranges.Ranges.SequenceEqual(new[]{new IdRange(10,12)}),"adjacent receipts merge from both sides");
        check(ranges.Add(11)==null,"duplicate receipt does not grow metadata");
        ranges.Add(int.MaxValue); check(ranges.Contains(int.MaxValue),"receipt handles max ID without overflow");
        reject(()=>ranges.Add(0),"zero ID refused");
        var stream=new IdRanges(); for(int i=1;i<=100000;i++) stream.Add(i);
        check(stream.Ranges.Count==1,"100000 consecutive receipts use one range");
        near(stream.NextAvailable(1,0,10_000_000),100001,"restart allocator skips consumed IDs");
        stream.AddRange(10_000_001,10_000_010);
        near(stream.NextAvailable(10_000_000,10_000_000,10_000_000),10_000_011,"assigned block is a base offset, not an index");
        near(stream.NextAvailable(20_000_000,20_000_000,10_000_000),20_000_000,"unrelated block unaffected");
        reject(()=>stream.NextAvailable(10,0,10),"exhausted block stops without wrapping");

        for(int i=0;i<5000;i++)
        {
            bool same=i%2==0; double source=1+random.NextDouble()*200, original=source;
            double bag=same?source:random.NextDouble()*50, oldBag=bag, cargo=0;
            double requested=random.NextDouble()*source, fraction=i%3==0?0:i%3==1?0.5:1;
            double ReadBag()=>same?source:bag;
            void WriteBag(double v) {if(same)source=v; else bag=v;}
            double result=HandoffCommit.Apply(requested,same,()=>source,v=>source=v,ReadBag,WriteBag,
                q=>{cargo=q*fraction;WriteBag(ReadBag()-cargo);return cargo;},()=>cargo=0);
            near(result,cargo,"native partial pack quantity respected");
            near(source+cargo,original,"handoff preserves donor mass");
            near(ReadBag(),same?oldBag-cargo:oldBag,"temporary bag returns to original stock");
        }
        foreach(string fault in new[]{"throws","wrong-debit","wrong-amount"})
        {
            double donor=20,bag=3,cargo=0;
            reject(()=>HandoffCommit.Apply(5,false,()=>donor,v=>donor=v,()=>bag,v=>bag=v,q=>
            { cargo=q; if(fault=="throws") throw new IOException("native pack failed"); if(fault!="wrong-debit")bag-=q; return fault=="wrong-amount"?q+1:q; },()=>cargo=0),"failed native packing rolled back");
            near(donor,20,"failed handoff restores donor"); near(bag,3,"failed handoff restores temporary bag"); near(cargo,0,"unpublished cargo discarded");
        }

        // Fault model of the production protocol: one ordered atomic frame, receipt before ACK.
        // This tests the model; native callback and Steam delivery still require gameplay tests.
        var accepted=new IdRanges(); double mirror=20,hostCargo=0,delivered=0; int applications=0;
        void Receive(int id,double finalSource,double kg)
        { if(accepted.Contains(id))return; mirror=finalSource;hostCargo+=kg;accepted.Add(id);applications++; }
        Receive(100,15,5); delivered+=hostCargo;hostCargo=0; // lost ACK, target has already consumed cargo
        mirror=12; // a later native player report has arrived
        Receive(100,15,5);
        near(delivered,5,"lost ACK retry does not deliver twice"); near(mirror,12,"duplicate frame cannot revert newer absolute source snapshot");
        check(applications==1&&hostCargo==0,"duplicate frame never recreates consumed cargo");
        var saved=accepted.Ranges.ToArray(); accepted=new();foreach(var r in saved)accepted.AddRange(r.First,r.Last);
        Receive(100,15,5);check(applications==1,"replay after save/load stays deduplicated");

        var prefixes=new PrefixIndex<int>(); prefixes.Add("pack:1:",1);prefixes.Add("pack:1:extra:",2);prefixes.Add("pack:10:",10);
        check(prefixes.TryMatch("pack:1:extra:owner",out int found,out int length)&&found==2&&length==13,"longest prefix wins without per-packager scans");
        check(prefixes.TryMatch("pack:10:owner",out found,out _)&&found==10,"numeric prefix boundary respected");
        check(!prefixes.TryMatch("other:owner",out _,out _),"unknown storage untouched");
        for(int i=0;i<3000;i++)
        {
            var sources=Enumerable.Range(0,random.Next(1,30)).Select(n=>new SourceOffer(n,-1,random.NextDouble()*100)).ToArray();
            var targets=Enumerable.Range(1,random.Next(1,40)).Select(n=>new ReceiverPort(n,ResourceKind.Dust,random.NextDouble()*50,random.Next(-1,2))).ToArray();
            int limit=random.Next(1,100); var plan=BatchRouter.Plan(ResourceKind.Dust,sources,targets,limit);
            check(plan.Count<=limit,"per-tick move limit respected");
            foreach(var s in sources)check(plan.Where(m=>m.SourceIndex==s.Index).Sum(m=>m.Kilograms)<=s.Kilograms+1e-7,"batch cannot overdraw source");
            foreach(var t in targets)check(plan.Where(m=>m.ReceiverId==t.MachineId).Sum(m=>m.Kilograms)<=t.FreeKilograms+1e-7,"batch cannot overfill receiver");
            var full=BatchRouter.Plan(ResourceKind.Dust,sources,targets,int.MaxValue);
            near(full.Sum(m=>m.Kilograms),Math.Min(sources.Sum(s=>s.Kilograms),targets.Sum(t=>t.FreeKilograms)),"aggregate allocation uses all available supply/space");
        }
        check(BatchRouter.Plan(ResourceKind.Dust,[new(0,1,5)],[new(1,ResourceKind.Dust,10)],128).Count==0,"self-loop cannot move inventory");
        check(BatchRouter.Plan(ResourceKind.Dust,[],[],128).Count==0,"empty catalog produces no movement");

        // Emulate persistent receiver rotation when the transfer limit truncates a plan.
        int cursor=0;var hits=new int[1000];
        for(int poll=0;poll<1000;poll++)
        {
            var targets=Enumerable.Range(0,1000).Select(n=>new ReceiverPort((cursor+n)%1000,ResourceKind.Dust,10)).ToArray();
            var plan=BatchRouter.Plan(ResourceKind.Dust,[new(0,-1,10000)],targets,16);
            foreach(var m in plan)hits[m.ReceiverId]++;
            cursor=(plan[^1].ReceiverId+1)%1000;
        }
        check(hits.Min()>0&&hits.Max()-hits.Min()<=1,"bounded scheduling rotation does not starve tail machines");
        foreach(int count in new[]{1,128,1000,10000})
        {
            var visited=new bool[count];int start=0;
            for(int poll=0;poll<count;poll++)
            {
                int actual=Math.Min(count,7); // time budget expires before the 128-source window ends
                for(int i=0;i<actual;i++)visited[(start+i)%count]=true;
                start=ScanCursor.Advance(start,actual,count);
            }
            check(visited.All(x=>x),"partial-window time budget never permanently skips catalog tail");
        }
    }

    internal static void Benchmark(string path)
    {
        var results=new List<object>();
        foreach(int size in new[]{100,1000,5000,10000})
        {
            var sources=Enumerable.Range(0,Math.Min(size,128)).Select(i=>new SourceOffer(i,-1,20)).ToArray();
            var ports=Enumerable.Range(0,size).Select(i=>new ReceiverPort(i,ResourceKind.Dust,1+i%20)).ToArray();
            for(int i=0;i<10;i++)BatchRouter.Plan(ResourceKind.Dust,sources,ports,128);
            var times=new double[101];int moves=0;
            for(int i=0;i<times.Length;i++) {long start=Stopwatch.GetTimestamp();moves=BatchRouter.Plan(ResourceKind.Dust,sources,ports,128).Count;times[i]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;}
            Array.Sort(times);results.Add(new{receivers=size,sources=sources.Length,maxMoves=128,moves,medianMs=times[50],p95Ms=times[95],maximumMs=times[^1]});
        }
        File.WriteAllText(path,JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,scope="Managed aggregate planner only; warm cache, 101 samples, no Unity/native inventory/Steam/logging/catalog cost. Not FPS or complete scheduler timing.",runtime=Environment.Version.ToString(),results},new JsonSerializerOptions{WriteIndented=true}));
    }
}
