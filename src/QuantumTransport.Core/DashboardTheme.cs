namespace QuantumTransport.Core;

public enum UiButtonTone { Default, Navigation, Primary, Warning }
public readonly record struct ButtonAppearance(UiColor Fill, UiColor Ink, UiColor Border, float Scale);

public static class DashboardTheme
{
    public static UiColor Blend(UiColor a,UiColor b,float t)
    {
        t=Math.Clamp(float.IsFinite(t)?t:0,0,1);
        byte Mix(byte x,byte y)=>(byte)Math.Round(x+(y-x)*t);
        return new(Mix(a.R,b.R),Mix(a.G,b.G),Mix(a.B,b.B),Mix(a.A,b.A));
    }
    public static float Follow(float current,float target,float delta,float seconds=0.075f) =>
        current+(target-current)*(1-MathF.Exp(-Math.Clamp(delta,0,0.1f)/Math.Max(seconds,0.001f)));
    private static double Luminance(UiColor c)
    {
        double Linear(byte value) {double v=value/255.0;return v<=0.04045?v/12.92:Math.Pow((v+0.055)/1.055,2.4);}
        return 0.2126*Linear(c.R)+0.7152*Linear(c.G)+0.0722*Linear(c.B);
    }
    public static double Contrast(UiColor a,UiColor b)
    {double x=Luminance(a),y=Luminance(b);return (Math.Max(x,y)+0.05)/(Math.Min(x,y)+0.05);}
    public static UiColor ReadableInk(UiColor fill)
    {
        // Pure endpoints keep >= 4.5:1 even through mid-luminance tween colors;
        // the softer page text colors have a contrast gap at that crossover.
        UiColor light=new(255,255,255),dark=new(0,0,0);
        return Contrast(fill,light)>=Contrast(fill,dark)?light:dark;
    }
    public static ButtonAppearance Button(UiNode node,bool hover,bool focus,bool pressed,float clickPulse=0)
    {
        UiColor fill=node.Tone switch
        {
            UiButtonTone.Primary=>new(83,222,207),
            UiButtonTone.Warning=>new(71,45,38),
            _=>new(28,46,61)
        };
        UiColor border=new(53,76,94);
        if(node.Selected) {fill=DashboardLayout.Cyan;border=DashboardLayout.Cyan;}
        if(node.Tone==UiButtonTone.Warning) border=new(130,85,60);
        if(!node.Enabled) return new(new(23,36,47),new(136,153,169),new(38,55,69),1);
        if(hover) {fill=Blend(fill,new(132,238,227),node.Selected||node.Tone==UiButtonTone.Primary?0.16f:0.22f);border=DashboardLayout.Cyan;}
        if(focus) border=new(155,247,237);
        if(pressed) fill=Blend(fill,new(44,121,135),0.22f);
        if(clickPulse>0) border=Blend(border,DashboardLayout.White,Math.Clamp(clickPulse,0,1));
        return new(fill,ReadableInk(fill),border,pressed?0.97f:clickPulse>0?1-0.018f*Math.Clamp(clickPulse,0,1):1);
    }
}

// Time comes from the mod's monotonic clock, never Unity timeScale. Inventory
// polling remains independent from these cheap visual-only transitions.
public sealed class DashboardMotion
{
    private float _rootTime,_pageTime;
    private float _closeStart=1;
    private int _pagePhase;
    public bool Closing {get;private set;}
    public bool Closed {get;private set;}
    public float RootAlpha {get;private set;}
    public float RootOffset=>12*(1-RootAlpha);
    public float ContentAlpha {get;private set;}=1;
    public float ContentOffset=>8*(1-ContentAlpha);
    public bool ContentInteractive=>_pagePhase==0 && !Closing;
    public bool SwapReady=>_pagePhase==2;
    private static float Ease(float t)=>1-MathF.Pow(1-Math.Clamp(t,0,1),3);
    public void Open(bool reduced)
    {
        Closing=false;Closed=false;_rootTime=0;_pageTime=0;_pagePhase=0;
        RootAlpha=reduced?1:0;ContentAlpha=1;
    }
    public void RequestClose(bool reduced)
    {
        if(Closing || Closed) return;
        Closing=true;_rootTime=0;_closeStart=RootAlpha;
        if(reduced) {RootAlpha=0;Closed=true;}
    }
    public void RequestPage(bool reduced)
    {
        if(Closing || Closed || _pagePhase!=0) return;
        _pageTime=0;_pagePhase=reduced?2:1;
        if(reduced) ContentAlpha=0;
    }
    public void CommitPage(bool reduced)
    {
        _pageTime=0;_pagePhase=reduced?0:3;ContentAlpha=reduced?1:0;
    }
    public void Advance(float delta,bool reduced)
    {
        delta=Math.Clamp(float.IsFinite(delta)?delta:0,0,0.1f);
        if(Closed) return;
        _rootTime+=delta;
        if(Closing)
        {
            RootAlpha=reduced?0:_closeStart*(1-Ease(_rootTime/0.13f));
            if(reduced || _rootTime>=0.13f) {RootAlpha=0;Closed=true;}
            return;
        }
        RootAlpha=reduced?1:Ease(_rootTime/0.19f);
        _pageTime+=delta;
        if(_pagePhase==1)
        {
            ContentAlpha=reduced?0:1-Ease(_pageTime/0.07f);
            if(reduced || _pageTime>=0.07f) {_pagePhase=2;ContentAlpha=0;}
        }
        else if(_pagePhase==3)
        {
            ContentAlpha=reduced?1:Ease(_pageTime/0.13f);
            if(reduced || _pageTime>=0.13f) {_pagePhase=0;ContentAlpha=1;}
        }
    }
}
