using System.Globalization;

namespace QuantumTransport.Core;

public sealed record DashboardSource(string Name, int Count, double Kilograms, int Sampled=0);
public sealed record DashboardMachine(int Id, string Name, string State, double Dust, double DustCapacity,
    double Sand, double SandCapacity, double Progress, int Cycles);
public sealed record DashboardData(bool WorldReady, bool Enabled, string Role, string State,
    double DustMoved, double SandMoved, double StoneDelivered, double GarageMoved, double DustDemand,
    int SourcesSampled, int SourcesTotal, DashboardSource[] Sources, int MachinesTotal, DashboardMachine[] Machines,
    double TickMilliseconds, long BudgetYields, int InFlight, int PendingPackets, long RejectedPackets,
    string Distribution, int ChamberPriority, int PressPriority, double PressBuffer, double PackagerReserve,
    bool PlayerEnabled, bool VehicleEnabled, bool RobotEnabled, bool GarageEnabled, bool StoneEnabled,
    string? Error = null, bool Cached = false);

public enum DashboardTab { Overview, Machines, Rules }
public enum UiKind { Box, Text, Button }
public readonly record struct UiColor(byte R, byte G, byte B, byte A = 255);
public sealed record UiNode(string Id, UiKind Kind, float X, float Y, float Width, float Height,
    UiColor Color, string Text = "", int FontSize = 16, bool Bold = false, string Action = "")
{
    public bool Content {get;init;}
    public bool Enabled {get;init;}=true;
    public bool Selected {get;init;}
    public UiButtonTone Tone {get;init;}
    public string Hint {get;init;}="";
}

// This scene description is shared by the actual Unity renderer and offline
// layout previews. No Unity objects, transport mutations, or file IO here.
public static class DashboardLayout
{
    public const int Width = 1120, Height = 740, MachinesPerPage = 7;
    public static readonly UiColor Background = new(13, 22, 31), Card = new(21, 34, 46),
        Cyan = new(98, 233, 220), White = new(232, 242, 246), Muted = new(141, 163, 179),
        Line = new(40, 59, 73), Orange = new(255, 185, 111);
    public static string Kg(double value) => double.IsFinite(value) ? value.ToString("N1", CultureInfo.InvariantCulture) + " kg" : "—";
    public static string StateLabel(string state) => state switch
    {
        "transferring"=>"正在转运", "waiting_for_supply"=>"等待资源", "demand_satisfied"=>"需求已满足",
        "no_compatible_receivers"=>"等待加工机器", "waiting_for_receiver_refresh"=>"等待需求刷新",
        "budget_yield"=>"分批调度中", "pending_save"=>"等待保存完成", "save_failed"=>"存档写入失败",
        "disabled"=>"运输已停用", "waiting_for_world"=>"等待进入世界", "co_op_disabled"=>"联机运输未启用",
        "client_joining"=>"正在加入主机", "client_waiting_for_host_mod"=>"等待主机启用运输",
        "client_backpressure"=>"等待需求或交接回执", "client_handoff"=>"正在交接本机资源",
        "client_waiting_for_supply"=>"等待本机采集资源",
        "world_initializing"=>"世界初始化中", "error_disabled"=>"会话故障已停用", "unsupported_build"=>"游戏版本不受支持",
        _=>state
    };
    public static int PageCount(int count) => Math.Max(1, (Math.Max(0, count) + MachinesPerPage - 1) / MachinesPerPage);
    public static float FitScale(int width, int height) => Math.Max(0.1f, Math.Min(width / 1180f, height / 800f));

    public static IReadOnlyList<UiNode> Build(DashboardData d, DashboardTab tab, int page)
    {
        var n = new List<UiNode>();
        bool content=false;
        void Box(string id,float x,float y,float w,float h,UiColor c) => n.Add(new(id,UiKind.Box,x,y,w,h,c){Content=content});
        void Text(string id,string text,float x,float y,float w,float h,int size=16,UiColor? color=null,bool bold=false) =>
            n.Add(new(id,UiKind.Text,x,y,w,h,color??White,text,size,bold){Content=content});
        void Button(string id,string text,float x,float y,float w,float h,string action,bool selected=false,bool enabled=true,UiButtonTone tone=UiButtonTone.Default,string hint="") =>
            n.Add(new(id,UiKind.Button,x,y,w,h,selected?Cyan:Card,text,15,selected,action){Content=content,Selected=selected,Enabled=enabled,Tone=tone,Hint=hint});
        void Metric(string id,string label,string value,float x,UiColor accent)
        {
            Box(id+".bg",x,108,282,106,Card); Box(id+".accent",x,108,3,106,accent);
            Text(id+".label",label,x+18,122,246,22,14,Muted);
            Text(id+".value",value,x+18,151,246,44,30,White,true);
        }
        Box("shadow",0,0,Width,Height,new(0,0,0,225));
        Box("background",1,1,Width-2,Height-2,Background);
        Box("sidebar",1,1,181,Height-2,new(17,29,40));
        Box("topline",0,0,Width,3,Cyan);
        Box("logo",24,26,46,46,Cyan); Text("logo.text","QT",32,35,38,30,23,Background,true);
        Text("title","量子传送",82,34,92,32,20,White,true);
        Text("subtitle","QUANTUM TRANSPORT",24,86,142,18,10,Muted);
        Text("version","v0.7.0  /  月球物流",24,108,142,18,11,Muted);
        Box("status.badge",690,26,246,39,Card);
        Text("status",d.WorldReady?StateLabel(d.State):"等待进入世界",704,34,218,26,14,d.Error!=null?Orange:Cyan,true);
        Button("close","关闭面板  ×",956,26,138,40,"close",hint:"关闭此面板，返回原界面；不会退出游戏。");
        Text("nav.label","运输控制台",24,141,140,20,12,Muted);
        foreach(var (label,index) in new[]{("运输总览",0),("机器状态",1),("分配规则",2)})
            Button("nav."+index,label,18,174+index*55,146,43,"tab:"+index,(int)tab==index,tone:UiButtonTone.Navigation,
                hint:index==0?"查看采集来源、库存样本及运输统计。":index==1?"查看加工机输入和进度；可使用 PgUp / PgDn 翻页。":"查看当前供料策略、保留量及来源开关。");
        Box("nav.indicator",8,184+(int)tab*55,3,23,Cyan);
        Text("transport.state",d.Enabled?"量子运输 · 已启用":"量子运输 · 已关闭",24,358,140,24,13,d.Enabled?Cyan:Orange,true);
        Button("transport.toggle",d.Enabled?"关闭量子传送":"启用量子传送",18,393,146,43,"toggle_transport",tone:d.Enabled?UiButtonTone.Warning:UiButtonTone.Primary,
            hint:d.Enabled?"停止新增量子转运；保留机器和来源现有库存，并保存配置。":"恢复按需量子转运，并保存配置；无需关闭面板。");
        Text("transport.tip","切换立即生效并保存",24,449,140,36,11,Muted);
        Box("sidebar.rule",24,548,134,1,Line);
        Text("sidebar.shortcuts","快捷键\n\nCtrl + F7   面板\nCtrl + F8   运输开关\nCtrl + F10  诊断快照",24,568,140,132,12,Muted);
        Text("section",tab switch{DashboardTab.Overview=>"全域运输 / OVERVIEW",DashboardTab.Machines=>"加工机器 / MACHINES",_=>"供料策略 / ROUTING"},206,36,480,28,17,White,true);
        Text("role",d.Role,206,73,700,20,13,Muted);
        bool client=d.Role=="联机客户端";
        Metric("metric.dust","本次会话累计 · 月尘转运",client?"主机统计":Kg(d.DustMoved),206,Cyan);
        Metric("metric.sand","本次会话累计 · 月砂转运",client?"主机统计":Kg(d.SandMoved),506,new(124,172,255));
        Metric("metric.stone","本次会话累计 · 月石交付",client?"主机统计":Kg(d.StoneDelivered),806,Orange);
        string notice = !d.WorldReady ? "进入存档后显示实际库存与机器状态。" : d.Role=="联机客户端" ?
            "客户端显示本机采集来源；机器供料与全局累计请在主机查看。" :
            "已确认库存按需传输 · 无距离限制 · 无接收需求时资源留在原处";
        if(d.Cached) notice="运输已关闭：来源索引为上次缓存，新建来源需重新开启后刷新。";
        if(d.Error!=null) notice="状态读取受限，请查看诊断日志："+d.Error;
        Text("notice",notice,206,229,882,42,13,d.Error!=null?Orange:Muted);
        content=true;
        if(tab==DashboardTab.Overview)
        {
            Box("sources.bg",206,277,536,360,Card);
            Text("sources.title","来源库存",224,292,200,28,18,White,true);
            Text("sources.sample",$"采样 {d.SourcesSampled} / {d.SourcesTotal} 条库存",454,297,270,22,12,Muted);
            int i=0;
            foreach(var source in d.Sources.Take(7))
            {
                float y=337+i++*40;
                Text("source."+i+".name",source.Name,224,y,214,28,15);
                Text("source."+i+".count",$"{source.Sampled}/{source.Count} 条",440,y,106,28,13,Muted);
                Text("source."+i+".kg",Kg(source.Kilograms),554,y,168,28,16,i==4?Cyan:White,true);
                Box("source."+i+".rule",224,y+32,498,1,Line);
            }
            Box("health.bg",760,277,328,360,Card);
            Text("health.title","调度与联机",780,292,284,28,18,White,true);
            string[] labels=["月尘待补需求", "本轮调度耗时", "预算让出次数", "交接中 / 待发包", "车库月尘已转运", "拒绝的交接包"];
            string[] values=[d.Role=="联机客户端"?"主机统一计算":Kg(d.DustDemand),d.TickMilliseconds.ToString("0.00",CultureInfo.InvariantCulture)+" ms",
                d.BudgetYields.ToString(CultureInfo.InvariantCulture),$"{d.InFlight} / {d.PendingPackets}",client?"主机统计":Kg(d.GarageMoved),d.RejectedPackets.ToString(CultureInfo.InvariantCulture)];
            for(i=0;i<labels.Length;i++)
            {
                Text("health."+i+".label",labels[i],780,340+i*44,174,24,13,Muted);
                Text("health."+i+".value",values[i],952,340+i*44,116,24,14,i==5&&d.RejectedPackets>0?Orange:White,true);
            }
            Text("sample.note","库存质量为最多 256 条来源的样本合计，产出含月砂/月石；条目数为索引总数，不等于设备数。",206,651,882,44,12,Muted);
        }
        else if(tab==DashboardTab.Machines)
        {
            Box("machines.bg",206,277,882,360,Card);
            string[] heads=["机器 / 状态","月尘输入","月砂输入","周期进度","完成次数"];
            int[] xs=[224,452,642,832,974];
            for(int i=0;i<heads.Length;i++) Text("head."+i,heads[i],xs[i],293,i==0?220:146,26,13,Muted);
            int row=0;
            foreach(var machine in d.Machines.Take(MachinesPerPage))
            {
                float y=337+row++*40;
                Text("machine."+row+".name",machine.Name+" #"+machine.Id+" · "+machine.State,224,y,220,28,14);
                Text("machine."+row+".dust",$"{machine.Dust:0.0} / {machine.DustCapacity:0.0} kg",452,y,182,28,14,Cyan);
                Text("machine."+row+".sand",machine.SandCapacity>0?$"{machine.Sand:0.0} / {machine.SandCapacity:0.0} kg":"—",642,y,184,28,14);
                Text("machine."+row+".progress",$"{Math.Clamp(machine.Progress,0,1):P0}",832,y,136,28,14,Muted);
                Text("machine."+row+".cycles",machine.Cycles.ToString(CultureInfo.InvariantCulture),974,y,96,28,14,Muted);
                Box("machine."+row+".rule",224,y+32,844,1,Line);
            }
            if(row==0) Text("machines.empty",d.Role=="联机客户端"?"此客户端不读取主机机器库存，请在主机打开此页。":"暂无可读取的加压舱或压石机。",224,367,824,50,16,Muted);
            int pages=PageCount(d.MachinesTotal);
            int currentPage=Math.Clamp(page,0,pages-1);
            Button("prev","上一页",206,654,112,36,"prev",enabled:currentPage>0,hint:currentPage==0?"已经是第一页。":"上一页机器 · PgUp");
            Text("pagination",$"{Math.Clamp(page,0,pages-1)+1} / {pages} 页   ·   {d.MachinesTotal} 台加工机器",338,659,510,28,14,Muted);
            Button("next","下一页",976,654,112,36,"next",enabled:currentPage<pages-1,hint:currentPage>=pages-1?"已经是最后一页。":"下一页机器 · PgDn");
        }
        else
        {
            Box("rules.bg",206,277,536,360,Card); Box("switches.bg",760,277,328,360,Card);
            Text("rules.title","月尘如何分配",224,293,480,30,18,White,true);
            string strategy=d.Distribution=="RecipeBalanced"?"按配方需求":"按输入容量";
            Text("rules.body",$"当前模式：{strategy}\n\n加压舱：补至月尘输入容量上限。\n压石机："+(d.Distribution=="RecipeBalanced"?$"月尘目标 = 未加工月砂 + {d.PressBuffer:0.0} kg。":"补至月尘输入容量上限。")+
                $"\n\n月尘优先级：加压舱 {d.ChamberPriority} / 压石机 {d.PressPriority}\n数值越大越优先；同级平均分配新增供应量。\n\n每台打包机保留：{Kg(d.PackagerReserve)}\n先调度月砂，再调度月尘；不抽回机器原有库存。",224,341,498,275,14,Muted);
            Text("switches.title","来源接入与交付",780,293,286,30,18,White,true);
            var switches=new[]{("人物 / 已存放容器",d.PlayerEnabled),("清扫车料斗",d.VehicleEnabled),("机器人 / 停靠站",d.RobotEnabled),("车库已卸货储仓",d.GarageEnabled),("月石交付站点",d.StoneEnabled)};
            for(int i=0;i<switches.Length;i++)
            {
                Text("switch."+i+".name",switches[i].Item1,780,344+i*45,214,28,14);
                Text("switch."+i+".value",switches[i].Item2?"开启":"关闭",1010,344+i*45,58,28,14,switches[i].Item2?Cyan:Muted,true);
            }
            Text("rules.note","此页只展示当前配置。月石交付后仍需原版发射结算；运输不直接增加余额。",206,651,882,44,12,Muted);
        }
        content=false;
        Box("footer.rule",206,709,882,1,Line);
        Text("footer","Tab / 方向键选择 · Enter 确认 · Esc / Ctrl+F7 返回 · 单人暂停时生产进度可能不变",206,717,882,18,11,Muted);
        return n;
    }
}
