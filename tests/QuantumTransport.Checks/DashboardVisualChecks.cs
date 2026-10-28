using QuantumTransport.Core;
using System.Text.Json;

internal static class DashboardVisualChecks
{
    internal static void Run(Action<bool,string> check,Action<double,double,string> near)
    {
        foreach(var tone in Enum.GetValues<UiButtonTone>())
        foreach(bool selected in new[]{false,true})
        foreach(bool enabled in new[]{false,true})
        foreach(bool hover in new[]{false,true})
        foreach(bool focus in new[]{false,true})
        foreach(bool pressed in new[]{false,true})
        {
            var node=new UiNode("test",UiKind.Button,0,0,146,43,DashboardLayout.Card,"测试按钮")
                {Tone=tone,Selected=selected,Enabled=enabled};
            var style=DashboardTheme.Button(node,hover,focus,pressed,1);
            check(DashboardTheme.Contrast(style.Fill,style.Ink)>=4.5,"all settled button states preserve readable text contrast");
            check(style.Scale>=0.96f && style.Scale<=1,"button feedback is bounded and never collapses its hit area");
            if(!enabled) near(style.Scale,1,"disabled buttons have no press animation");
            if(selected && enabled) check(style.Fill.G>120,"selected page never becomes a black button");
        }
        UiColor color=new(28,46,61);
        for(int i=0;i<100;i++)
        {
            color=DashboardTheme.Blend(color,DashboardLayout.Cyan,0.1f);
            check(DashboardTheme.Contrast(color,DashboardTheme.ReadableInk(color))>=4.5,"color interpolation chooses legible ink throughout transition");
        }
        var data=GarageDashboardChecks.Example;
        var first=DashboardLayout.Build(data,DashboardTab.Machines,0);
        check(!first.Single(n=>n.Action=="prev").Enabled && first.Single(n=>n.Action=="next").Enabled,"first machine page disables previous button");
        var last=DashboardLayout.Build(data,DashboardTab.Machines,DashboardLayout.PageCount(data.MachinesTotal)-1);
        check(last.Single(n=>n.Action=="prev").Enabled && !last.Single(n=>n.Action=="next").Enabled,"last machine page disables next button");
        var empty=DashboardLayout.Build(data with{MachinesTotal=0,Machines=[]},DashboardTab.Machines,0);
        check(!empty.Single(n=>n.Action=="prev").Enabled && !empty.Single(n=>n.Action=="next").Enabled,"empty machine list has no actionable pagination");
        foreach(var tab in Enum.GetValues<DashboardTab>())
        {
            var nodes=DashboardLayout.Build(data,tab,0);
            check(nodes.Where(n=>n.Kind==UiKind.Button).All(n=>n.Hint.Length>0),"every button explains its action or disabled boundary");
            check(nodes.Where(n=>n.Action.StartsWith("tab:")).Single(n=>n.Selected).Action=="tab:"+(int)tab,"current page has a persistent selection independent of keyboard focus");
            check(!nodes.Single(n=>n.Action=="close").Content && nodes.Where(n=>n.Content).All(n=>n.Y>=277),"content fades independently while close and navigation remain stable");
        }
        foreach(float step in new[]{1/144f,1/60f,1/20f,0.1f})
        {
            var motion=new DashboardMotion();motion.Open(false);float previous=0;
            for(int i=0;i<40;i++)
            {motion.Advance(step,false);check(motion.RootAlpha>=previous && motion.RootAlpha<=1,"panel opening alpha is monotonic and bounded");previous=motion.RootAlpha;}
            near(motion.RootAlpha,1,"opening completes across frame rates");
            motion.RequestPage(false);
            for(int i=0;i<40 && !motion.SwapReady;i++)motion.Advance(step,false);
            check(motion.SwapReady && !motion.ContentInteractive && motion.ContentAlpha==0,"page content swaps only while hidden");
            motion.CommitPage(false);
            for(int i=0;i<40;i++)motion.Advance(step,false);
            check(motion.ContentInteractive && motion.ContentAlpha==1,"new page becomes fully visible and interactive");
            motion.RequestClose(false);previous=1;
            for(int i=0;i<40;i++)
            {motion.Advance(step,false);check(motion.RootAlpha<=previous && motion.RootAlpha>=0,"panel closing alpha is monotonic and bounded");previous=motion.RootAlpha;}
            check(motion.Closed && !motion.ContentInteractive,"close animation reaches a terminal state before menu release");
        }
        var reduced=new DashboardMotion();reduced.Open(true);near(reduced.RootAlpha,1,"reduced motion opens immediately");
        reduced.RequestPage(true);check(reduced.SwapReady,"reduced motion makes page swap immediately ready");reduced.CommitPage(true);
        near(reduced.ContentAlpha,1,"reduced motion does not leave content transparent");reduced.RequestClose(true);check(reduced.Closed,"reduced motion closes immediately");
        var interrupted=new DashboardMotion();interrupted.Open(false);interrupted.Advance(0.03f,false);interrupted.RequestClose(false);
        for(int i=0;i<30;i++)interrupted.Advance(0.02f,false);
        check(interrupted.Closed && interrupted.RootAlpha==0,"close during entry completes without stranding modal ownership");
    }

    internal static void WriteStates(string folder)
    {
        var node=new UiNode("demo",UiKind.Button,0,0,220,50,DashboardLayout.Card,"量子传送");
        var states=new[]{("普通",node,false,false,false),("悬停",node,true,false,false),("按下",node,true,false,true),
            ("键盘焦点",node,false,true,false),("当前页面",node with{Selected=true},false,false,false),("不可点击",node with{Enabled=false},false,false,false)};
        File.WriteAllText(Path.Combine(folder,"dashboard-interactions.json"),JsonSerializer.Serialize(new{sampleData=true,
            states=states.Select(s=>new{label=s.Item1,style=DashboardTheme.Button(s.Item2,s.Item3,s.Item4,s.Item5)})},new JsonSerializerOptions{WriteIndented=true}));
    }
}
