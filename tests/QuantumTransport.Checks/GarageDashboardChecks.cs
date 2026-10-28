using QuantumTransport.Core;
using System.Text.Json;

internal static class GarageDashboardChecks
{
    internal static DashboardData Example => new(true,true,"联机主机","transferring",1248.6,426.0,198.5,184.2,36.0,52,52,
        [new("人物 / 容器",2,0.6,2),new("清扫车料斗",3,12.4,3),new("机器人 / 停靠站",12,8.2,12),new("车库已卸货储仓",4,37.5,4),
         new("打包机",6,4.0,6),new("机器产出",18,14.2,18),new("散落 / 交接胶囊",7,2.0,7)],24,
        Enumerable.Range(1,7).Select(i=>new DashboardMachine(i,i%2==0?"压石机":"加压舱",i%3==0?"待机":"加工中",
            i%2==0?3.2:12.0,i%2==0?12:20,i%2==0?4.5:0,i%2==0?12:0,i*0.11,i*14)).ToArray(),
        1.42,8,2,1,0,"RecipeBalanced",0,0,1,0,true,true,true,true,true);

    internal static void Run(Action<bool,string> check, Action<double,double,string> near,Action<Action,string> reject)
    {
        var lease=new DashboardMenuLease();
        int resumes=0,dismisses=0,restores=0;
        lease.Acquire(()=>resumes++,()=>restores++);
        lease.OwnModal(()=>{dismisses++; lease.Release();});
        lease.Release(); lease.Release();
        check(!lease.Active && resumes==1 && dismisses==1 && restores==0,"dashboard repeated/reentrant close resumes its own menu once");
        lease.Acquire(null,()=>restores++); lease.OwnModal(()=>dismisses++); lease.Release();
        check(resumes==1 && restores==1 && dismisses==2,"dashboard preserves pre-existing pause menu and restores its selection");
        lease.Acquire(()=>resumes++,null);
        lease.OwnModal(()=>throw new IOException("simulated modal hide failure"));
        reject(lease.Release,"dashboard modal cleanup reports failure");
        check(!lease.Active && resumes==2,"dashboard still releases its owned menu after modal failure");
        lease.Release();
        check(resumes==2,"failed modal cleanup cannot trigger a second resume");
        reject(()=>lease.OwnModal(()=>{}),"closed dashboard cannot take ownership of an unrelated modal");
        lease.Acquire(null,null);
        reject(()=>lease.Acquire(null,null),"dashboard cannot overwrite an active menu lease"); lease.Release();
        foreach(string unsafeAction in new[]{"quit","CloseWindow","RequestQuit","RequestMainMenu","tab:3","tab:-1",""})
            check(!DashboardActions.TryParse(unsafeAction,out _),"dashboard rejects unknown or exit-related action");
        var garages=new PrefixIndex<int>();
        garages.Add("garage-silo:1:",1); garages.Add("garage-silo:10:",10);
        check(MachineStorageIdentity.TryResolve(garages,"garage-silo:10:player-B","garage-silo:10:player-B",out int id,out string owner) && id==10 && owner=="player-B",
            "garage identity selects exact native prefix and preserves unloading contributor");
        check(MachineStorageIdentity.TryResolve(garages,"garage-silo:1:player-A","garage-silo:1:player-A",out id,out owner) && id==1 && owner=="player-A",
            "different contributors and neighboring garage IDs remain distinct");
        check(!MachineStorageIdentity.TryResolve(garages,"garage-silo:1:","garage-silo:1:",out _,out _),"ownerless garage inventory is not guessed from depot owner");
        check(!MachineStorageIdentity.TryResolve(garages,"garage-silo:1:player-A","garage-silo:1:player-B",out _,out _),"mismatched garage snapshot ID is rejected");
        check(!MachineStorageIdentity.TryResolve(garages,"vehicle:player-A","vehicle:player-A",out _,out _),"vehicle and garage inventories cannot overlap");
        check(!MachineStorageIdentity.TryResolve(garages,"garage-silo:1:"+new string('x',129),"garage-silo:1:"+new string('x',129),out _,out _),"invalid oversized garage contributor rejected");
        var stocks=new Dictionary<string,double>{{"player-A",10},{"player-B",6}};
        var target=new Dictionary<string,double>{{"player-A",0},{"player-B",0}};
        foreach(string contributor in stocks.Keys)
        {
            string key="garage-silo:1:"+contributor;
            check(MachineStorageIdentity.TryResolve(garages,key,key,out _,out owner),"garage transfer identity resolves");
            string selectedOwner=owner;
            TransferCommit.Apply(3,()=>stocks[selectedOwner],v=>stocks[selectedOwner]=v,()=>target[selectedOwner],v=>target[selectedOwner]+=v,()=>target[selectedOwner]=0);
        }
        near(stocks["player-A"],7,"garage debit only original contributor A");
        near(stocks["player-B"],3,"garage debit only original contributor B");
        near(target["player-A"]+target["player-B"],6,"garage machine inputs preserve total transferred mass");
        double original=stocks["player-A"],previous=target["player-A"];
        reject(()=>TransferCommit.Apply(1,()=>stocks["player-A"],v=>stocks["player-A"]=v,()=>target["player-A"],
            v=>{target["player-A"]+=v;throw new IOException("injected write failure");},()=>target["player-A"]=previous),"garage failed native target write surfaces");
        near(stocks["player-A"],original,"garage failure restores source"); near(target["player-A"],previous,"garage failure restores per-owner target");
        check(CoopPolicy.MayRead(TransportRole.Host,"garage","remote-contributor",null,"host") &&
            !CoopPolicy.MayRead(TransportRole.Client,"garage","client",null,"client"),"shared garage storage is host-authoritative");
        var supply=BatchRouter.Plan(ResourceKind.Dust,[new(0,1,10)],
            [new(20,ResourceKind.Dust,20),new(30,ResourceKind.Dust,FeedPolicy.PressDustSpace(0,12,0,1,DustDistribution.RecipeBalanced))],128);
        near(supply.Single(m=>m.ReceiverId==20).Kilograms,9,"garage supply obeys existing chamber fair allocation");
        near(supply.Single(m=>m.ReceiverId==30).Kilograms,1,"garage supply obeys sandless press recipe limit");

        string[] categories=[..Enumerable.Repeat("machines",10000),"player","garage","vehicle"];
        var sample=DashboardSampling.Select(categories,256);
        check(sample.Count==256 && sample.Distinct().Count()==256,"dashboard native inventory read bound and no duplicate samples");
        check(sample.Any(i=>categories[i]=="player") && sample.Any(i=>categories[i]=="garage") && sample.Any(i=>categories[i]=="vehicle"),"large factory cannot hide smaller dashboard source groups");
        check(DashboardSampling.Select([],256).Count==0 && DashboardSampling.Select(categories,0).Count==0,"empty dashboard sampling needs no native reads");
        reject(()=>DashboardSampling.Select(categories,-1),"negative dashboard read budget rejected");
        check(DashboardLayout.PageCount(0)==1 && DashboardLayout.PageCount(7)==1 && DashboardLayout.PageCount(8)==2,"machine pagination handles empty and boundary pages");
        foreach(var (width,height) in new[]{(1920,1080),(1280,720),(1024,768),(3840,1080),(600,900)})
        {
            float scale=DashboardLayout.FitScale(width,height);
            check(scale*DashboardLayout.Width<=width && scale*DashboardLayout.Height<=height,"dashboard fits supported screen aspect ratio");
        }
        foreach(var data in new[]{Example,Example with{WorldReady=false,State="waiting_for_world",Sources=[],SourcesSampled=0,SourcesTotal=0,Machines=[],MachinesTotal=0},
            Example with{Role="联机客户端",Machines=[],MachinesTotal=0}, Example with{Enabled=false,State="disabled",Cached=true},
            Example with{State="error_disabled",Error="simulated diagnostic failure"}})
        foreach(var tab in Enum.GetValues<DashboardTab>())
        {
            var nodes=DashboardLayout.Build(data,tab,99);
            check(nodes.Select(n=>n.Id).Distinct().Count()==nodes.Count,"dashboard stable unique node identities");
            foreach(var n in nodes)
                check(n.X>=0 && n.Y>=0 && n.Width>0 && n.Height>0 && n.X+n.Width<=DashboardLayout.Width && n.Y+n.Height<=DashboardLayout.Height,
                    "dashboard node is within frame: "+n.Id);
            check(nodes.Any(n=>n.Action=="close"),"dashboard always exposes close action");
            check(nodes.Where(n=>n.Kind==UiKind.Button).All(n=>DashboardActions.TryParse(n.Action,out _)),
                "dashboard only emits validated navigation and transport-switch commands");
            var toggle=nodes.Single(n=>n.Action=="toggle_transport");
            check(toggle.Text==(data.Enabled?"关闭量子传送":"启用量子传送"),"dashboard toggle label reflects real transport state on every page");
        }
        check(DashboardLayout.Build(Example with{Role="联机客户端"},DashboardTab.Overview,0).Any(n=>n.Text.Contains("本机采集")),"client-only statistics scope labeled");
        check(DashboardLayout.Build(Example,DashboardTab.Overview,0).Any(n=>n.Text.Contains("样本合计")),"sampled inventory is not presented as global mass");
        check(DashboardLayout.Kg(double.NaN)=="—","invalid dashboard mass never formats as real stock");
    }

    internal static void WritePreviews(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach(var tab in Enum.GetValues<DashboardTab>())
            File.WriteAllText(Path.Combine(folder,"dashboard-"+tab.ToString().ToLowerInvariant()+".json"),
                JsonSerializer.Serialize(new{sampleData=true,scope="Offline layout preview using the same scene description as the Unity dashboard; not a gameplay screenshot.",
                    width=DashboardLayout.Width,height=DashboardLayout.Height,nodes=DashboardLayout.Build(Example,tab,0),
                    buttonStyles=DashboardLayout.Build(Example,tab,0).Where(n=>n.Kind==UiKind.Button).ToDictionary(n=>n.Id,n=>DashboardTheme.Button(n,false,false,false))},new JsonSerializerOptions{WriteIndented=true}));
    }
}
