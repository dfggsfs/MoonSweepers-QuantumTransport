using Il2CppMoonCleaners;
using Il2CppTMPro;
using QuantumTransport.Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Diagnostics;
using UObject = UnityEngine.Object;

namespace QuantumTransport;

// Native UGUI/TextMeshPro with the game's font/material and rounded panels.
// No production/input detours, injected MonoBehaviours, or world inventory writes.
internal sealed class Dashboard(QuantumMod mod) : IDisposable
{
    private sealed class Part(GameObject root,RectTransform rect,TMP_Text? label,MoonHudPanel? panel,Button? button,UiNode node,MoonHudPanel? border=null,RectTransform? visual=null)
    {
        internal readonly GameObject Root=root;
        internal readonly RectTransform Rect=rect;
        internal readonly TMP_Text? Label=label;
        internal readonly MoonHudPanel? Panel=panel,Border=border;
        internal readonly Button? Button=button;
        internal readonly RectTransform? Visual=visual;
        internal UiNode Node=node;
        internal float Pulse,Scale=1,Y=node.Y;
        internal UiColor Fill=DashboardTheme.Button(node,false,false,false).Fill;
        internal UiColor Ink=DashboardTheme.Button(node,false,false,false).Ink,Stroke=DashboardTheme.Button(node,false,false,false).Border;
    }
    private readonly Dictionary<string,Part> _parts=[];
    private GameObject? _canvas;
    private RectTransform? _frame;
    private RectTransform? _content;
    private CanvasGroup? _canvasGroup,_frameGroup,_contentGroup;
    private readonly DashboardMotion _motion=new();
    private MoonMenuKit? _kit;
    private MoonPauseMenu? _menu;
    private readonly DashboardMenuLease _lease=new();
    private bool _openedModal;
    private long _nextRefresh;
    private DashboardTab _tab;
    private int _page,_machineCount;
    private DashboardTab _pendingTab;
    private int _pendingPage;
    private bool _pendingRoute;
    private long _lastVisual,_feedbackUntil;
    private string _feedback="";
    internal bool Visible { get; private set; }
    internal string? LastError { get; private set; }

    internal void Toggle()
    {
        if(Visible) { RequestClose(); return; }
        if(!mod.Settings.EnableDashboard) return;
        try
        {
            var world=mod.CurrentWorld;
            if(world==null || !(bool)MoonSession.InGame) return;
            // Let the original pause/menu system own cursor, gameplay blocking and
            // EventSystem lifetime. Never overwrite UIBlocked or cursor snapshots.
            var walkers=UObject.FindObjectsByType<MoonWalker>(FindObjectsSortMode.None);
            MoonWalker? local=null;
            for(int i=0;i<(int)walkers.Length;i++)
                if(walkers[i]!=null && walkers[i].world==world && (string)walkers[i].UpgradePlayer==mod.Coop.LocalPlayer)
                { local=walkers[i]; break; }
            if(local==null || local!.PauseMenu==null) return;
            _menu=local.PauseMenu;
            if((bool)_menu!.ModalOpen || (bool)_menu.IsSaving || (bool)_menu.DisplayConfirmationPending) { _menu=null; return; }
            bool openedMenu=!(bool)_menu.IsOpen;
            if(openedMenu && !(bool)_menu.Open()) { _menu=null; return; }
            var menu=_menu;
            var previousSelection=EventSystem.current==null?null:EventSystem.current!.currentSelectedGameObject;
            _lease.Acquire(openedMenu?()=>{if(menu!=null && (bool)menu!.IsOpen) menu.Resume();}:null,
                !openedMenu?()=>{if(previousSelection!=null && EventSystem.current!=null) EventSystem.current!.SetSelectedGameObject(previousSelection);}:null);
            _lease.OwnModal(()=>
            {
                // This is our modal; remove callbacks before hiding it. No quit,
                // leave-world or close-application API belongs in this path.
                if(menu==null) return;
                if(menu!.dialog!=null) {menu.dialog!.onAccept=null; menu.dialog.onCancel=null;}
                menu.modalAccept=null; menu.modalCancel=null;
                if((bool)menu.ModalOpen) menu.HideModal();
            });
            _menu.ShowModal("量子传送", "运输状态面板", "", (Action)(()=>{}), "", (Action)(()=>{}));
            _openedModal=true;
            // Native dialogs also poll Enter/Space/gamepad submit independently
            // of the selected UGUI button. They must not consume dashboard tabs,
            // paging or toggle actions. Our own buttons/ESC own panel actions.
            if(_menu.dialog==null) throw new InvalidOperationException("Native dashboard modal was not created");
            _menu.dialog!.onAccept=null;
            _menu.dialog.onCancel=null;
            _menu.modalAccept=null; _menu.modalCancel=null;
            EnsureCanvas();
            LastError=null; Visible=true; _pendingRoute=false;
            _motion.Open(mod.Settings.ReduceDashboardMotion); _lastVisual=Stopwatch.GetTimestamp();
            _canvasGroup!.alpha=_motion.RootAlpha;
            _canvas!.SetActive(true); _nextRefresh=0; _feedbackUntil=0;
            Update();
            if(EventSystem.current!=null && _parts.TryGetValue("nav."+(int)_tab,out var firstTab)) EventSystem.current!.SetSelectedGameObject(firstTab.Root);
        }
        catch(Exception error) { Fault(error); }
    }

    internal void Update()
    {
        if(!Visible) return;
        try
        {
            var keyboard=UnityEngine.InputSystem.Keyboard.current;
            if(keyboard!=null && (bool)keyboard.escapeKey.wasPressedThisFrame) RequestClose();
            if(!Visible) return;
            if(!mod.Settings.EnableDashboard || mod.CurrentWorld==null || _menu==null || !(bool)_menu!.IsOpen ||
                (_openedModal && !(bool)_menu.ModalOpen) || _canvas==null)
            { Close(); return; }
            var canvas=_canvas!.GetComponent<Canvas>();
            canvas.scaleFactor=DashboardLayout.FitScale(Screen.width,Screen.height);
            long ticks=Stopwatch.GetTimestamp();
            float delta=(float)Stopwatch.GetElapsedTime(_lastVisual,ticks).TotalSeconds; _lastVisual=ticks;
            _motion.Advance(delta,mod.Settings.ReduceDashboardMotion);
            if(_motion.Closed) {Close();return;}
            if(_motion.SwapReady && _pendingRoute)
            {
                _tab=_pendingTab; _page=_pendingPage; _pendingRoute=false;
                RefreshData(); _motion.CommitPage(mod.Settings.ReduceDashboardMotion);
            }
            if(_pendingRoute && _motion.ContentInteractive) _motion.RequestPage(mod.Settings.ReduceDashboardMotion);
            if(!_motion.Closing && keyboard!=null && _tab==DashboardTab.Machines && (!_pendingRoute || _pendingTab==DashboardTab.Machines))
            {
                if((bool)keyboard.pageUpKey.wasPressedThisFrame) Act("prev");
                if((bool)keyboard.pageDownKey.wasPressedThisFrame) Act("next");
            }
            long now=Environment.TickCount64;
            if(!_motion.Closing && !_pendingRoute && now>=_nextRefresh) RefreshData();
            Animate(delta);
        }
        catch(Exception error) { Fault(error); }
    }

    private void RefreshData()
    {
        _nextRefresh=Environment.TickCount64+1000;
        var data=mod.Manager.CaptureDashboard(_page); _machineCount=data.MachinesTotal;
        _page=Math.Clamp(_page,0,DashboardLayout.PageCount(_machineCount)-1);
        Render(DashboardLayout.Build(data,_tab,_page));
    }

    private void RequestClose()
    {
        _pendingRoute=false;
        _motion.RequestClose(mod.Settings.ReduceDashboardMotion);
        if(_frameGroup!=null) _frameGroup!.interactable=false;
        if(_motion.Closed) Close();
    }

    private void EnsureCanvas()
    {
        if(_canvas!=null) return;
        _parts.Clear(); _kit?.Dispose();
        _kit=new MoonMenuKit("QuantumTransport.Dashboard");
        _canvas=MoonMenuKit.Canvas(null,"QuantumTransport.Dashboard",31000);
        var scaler=_canvas.GetComponent<CanvasScaler>();
        if(scaler!=null) scaler!.enabled=false;
        var canvas=_canvas.GetComponent<Canvas>();
        canvas.scaleFactor=DashboardLayout.FitScale(Screen.width,Screen.height);
        _canvasGroup=_canvas.AddComponent<CanvasGroup>();
        var blocker=_kit.Panel(_canvas.transform,"Dashboard.InputShield",0,0,1,1,new Color(0.015f,0.025f,0.04f,0.86f),0);
        blocker.raycastTarget=true;
        var shield=blocker.GetComponent<RectTransform>();
        shield.anchorMin=new Vector2(0,0); shield.anchorMax=new Vector2(1,1);
        shield.offsetMin=new Vector2(0,0); shield.offsetMax=new Vector2(0,0);
        _frame=_kit.Rect(_canvas.transform,"Dashboard.Frame",0,0,DashboardLayout.Width,DashboardLayout.Height);
        _frame.anchorMin=new Vector2(0.5f,0.5f); _frame.anchorMax=new Vector2(0.5f,0.5f);
        _frame.pivot=new Vector2(0,1);
        _frame.anchoredPosition=new Vector2(-DashboardLayout.Width/2f,DashboardLayout.Height/2f);
        _frameGroup=_frame.gameObject.AddComponent<CanvasGroup>();
        _content=_kit.Rect(_frame,"Dashboard.Content",0,0,DashboardLayout.Width,DashboardLayout.Height);
        _contentGroup=_content.gameObject.AddComponent<CanvasGroup>();
    }

    private static Color ColorOf(UiColor c)=>new(c.R/255f,c.G/255f,c.B/255f,c.A/255f);
    private void Render(IReadOnlyList<UiNode> nodes)
    {
        var active=nodes.Select(node=>node.Id).ToHashSet(StringComparer.Ordinal);
        foreach(var pair in _parts) if(!active.Contains(pair.Key)) pair.Value.Root.SetActive(false);
        foreach(var node in nodes)
        {
            if(!_parts.TryGetValue(node.Id,out var part))
            {
                if(node.Kind==UiKind.Box)
                {
                    var panel=_kit!.Panel(node.Content?_content!:_frame!,node.Id,node.X,node.Y,node.Width,node.Height,ColorOf(node.Color),node.Height<=3?0:10);
                    panel.raycastTarget=true;
                    part=new(panel.gameObject,panel.GetComponent<RectTransform>(),null,panel,null,node);
                }
                else if(node.Kind==UiKind.Text)
                {
                    var text=_kit!.Label(node.Content?_content!:_frame!,node.Text,node.X,node.Y,node.Width,node.Height,node.FontSize,ColorOf(node.Color),node.Bold);
                    text.raycastTarget=false; text.richText=false;
                    text.alignment=TextAlignmentOptions.TopLeft;
                    text.textWrappingMode=TextWrappingModes.Normal;
                    text.overflowMode=TextOverflowModes.Ellipsis;
                    part=new(text.gameObject,text.GetComponent<RectTransform>(),text,null,null,node);
                }
                else
                {
                    // Flat panels use the normal UGUI graphic material. Never reuse
                    // the game's glass shader or let Selectable ColorTint multiply
                    // our palette into a black pressed/selected button.
                    var appearance=DashboardTheme.Button(node,false,false,false);
                    var border=_kit!.Panel(node.Content?_content!:_frame!,node.Id,node.X,node.Y,node.Width,node.Height,ColorOf(appearance.Border),10);
                    var fill=_kit.Panel(border.transform,node.Id+".fill",1,1,node.Width-2,node.Height-2,ColorOf(appearance.Fill),9);
                    border.raycastTarget=true; fill.raycastTarget=true;
                    border.material=null; fill.material=null;
                    var button=border.gameObject.AddComponent<Button>();
                    button.targetGraphic=fill; button.transition=Selectable.Transition.None;
                    button.interactable=node.Enabled;
                    string action=node.Action,id=node.Id;
                    button.onClick.AddListener((Action)(()=>Act(action,id)));
                    var visual=fill.GetComponent<RectTransform>();visual.pivot=new Vector2(0.5f,0.5f);
                    visual.anchoredPosition=new Vector2(node.Width/2,-node.Height/2);
                    var text=_kit.Label(fill.transform,node.Text,9,(node.Height-25)/2,node.Width-20,25,node.FontSize,ColorOf(appearance.Ink),true);
                    text.richText=false;text.raycastTarget=false;text.alignment=TextAlignmentOptions.Center;
                    text.overflowMode=TextOverflowModes.Ellipsis;
                    var rect=border.GetComponent<RectTransform>(); rect.pivot=new Vector2(0.5f,0.5f);
                    part=new(border.gameObject,rect,text,fill,button,node,border,visual);
                }
                _parts.Add(node.Id,part);
            }
            part.Node=node;
            if(!(bool)part.Root.activeSelf) part.Root.SetActive(true);
            // Reactivating changes UGUI selection in some game builds. Only update
            // changed strings/colors; never rebuild the whole UI every frame.
            if(part.Label!=null)
            {
                if((string)part.Label!.text!=node.Text) part.Label.text=node.Text;
                if(part.Button==null) part.Label.color=ColorOf(node.Color);
            }
            if(part.Panel!=null && part.Button==null) part.Panel!.color=ColorOf(node.Color);
            if(part.Button!=null) part.Button!.interactable=node.Enabled;
            Place(part,part.Y);
            part.Root.transform.SetAsLastSibling();
        }
        _content!.SetAsLastSibling();
        var selected=EventSystem.current==null?null:EventSystem.current!.currentSelectedGameObject;
        if(selected!=null && (!(bool)selected!.activeInHierarchy || _parts.Values.Any(p=>p.Root==selected && p.Button!=null && !p.Node.Enabled)))
            if(_parts.TryGetValue("nav."+(int)_tab,out var tab)) EventSystem.current!.SetSelectedGameObject(tab.Root);
    }

    private static void Place(Part part,float y)
    {
        var n=part.Node;
        part.Rect.anchoredPosition=part.Button==null?new Vector2(n.X,-y):new Vector2(n.X+n.Width/2,-y-n.Height/2);
    }

    private void Animate(float delta)
    {
        bool reduced=mod.Settings.ReduceDashboardMotion;
        _canvasGroup!.alpha=_motion.RootAlpha; _frameGroup!.interactable=!_motion.Closing;
        _contentGroup!.alpha=_motion.ContentAlpha; _contentGroup.interactable=_motion.ContentInteractive;
        _frame!.anchoredPosition=new Vector2(-DashboardLayout.Width/2f,DashboardLayout.Height/2f-_motion.RootOffset);
        float rootScale=reduced?1:0.986f+0.014f*_motion.RootAlpha;
        _frame.localScale=new Vector3(rootScale,rootScale,1);
        _content!.anchoredPosition=new Vector2(_motion.ContentOffset,0);
        var mouse=UnityEngine.InputSystem.Mouse.current;
        var keyboard=UnityEngine.InputSystem.Keyboard.current;
        var point=mouse==null?new Vector2(0,0):mouse!.position.ReadValue();
        bool mouseDown=mouse!=null && (bool)mouse!.leftButton.isPressed;
        bool submitDown=keyboard!=null && ((bool)keyboard!.enterKey.isPressed || (bool)keyboard.spaceKey.isPressed);
        var selected=EventSystem.current==null?null:EventSystem.current!.currentSelectedGameObject;
        string hint=""; bool pointerHint=false;
        foreach(var part in _parts.Values)
        {
            if(!(bool)part.Root.activeSelf) continue;
            if(part.Node.Id=="nav.indicator")
            {part.Y=reduced?part.Node.Y:DashboardTheme.Follow(part.Y,part.Node.Y,delta);Place(part,part.Y);}
            if(part.Button==null) continue;
            bool contentReady=!part.Node.Content || _motion.ContentInteractive;
            bool hover=contentReady && mouse!=null && (bool)RectTransformUtility.RectangleContainsScreenPoint(part.Rect,point,null);
            bool focus=contentReady && selected!=null && part.Root==selected;
            bool pressed=part.Node.Enabled && (hover && mouseDown || focus && submitDown);
            part.Pulse=Math.Max(0,part.Pulse-Math.Clamp(delta,0,0.1f)/0.22f);
            var appearance=DashboardTheme.Button(part.Node,hover,focus,pressed,part.Pulse);
            var fill=reduced?appearance.Fill:DashboardTheme.Blend(part.Fill,appearance.Fill,1-MathF.Exp(-Math.Clamp(delta,0,0.1f)/0.065f));
            if(fill!=part.Fill) {part.Panel!.color=ColorOf(fill);part.Fill=fill;}
            if(part.Stroke!=appearance.Border) {part.Border!.color=ColorOf(appearance.Border);part.Stroke=appearance.Border;}
            var ink=part.Node.Enabled?DashboardTheme.ReadableInk(part.Fill):appearance.Ink;
            if(ink!=part.Ink) {part.Label!.color=ColorOf(ink);part.Ink=ink;}
            float scale=reduced?1:DashboardTheme.Follow(part.Scale,appearance.Scale,delta,0.04f);
            // Shrink only the visual face; the outline/root remains a stable
            // raycast area so a click near an edge cannot fall out mid-press.
            if(Math.Abs(scale-part.Scale)>0.0001f) {part.Scale=scale;part.Visual!.localScale=new Vector3(scale,scale,1);}
            if(hover) {hint=part.Node.Hint;pointerHint=true;} else if(focus && !pointerHint) hint=part.Node.Hint;
        }
        if(_parts.TryGetValue("footer",out var footer) && footer.Label!=null)
        {
            string message=Environment.TickCount64<_feedbackUntil?_feedback:hint.Length>0?hint:footer.Node.Text;
            if((string)footer.Label!.text!=message) footer.Label.text=message;
            footer.Label.color=ColorOf(Environment.TickCount64<_feedbackUntil?DashboardLayout.Cyan:DashboardLayout.Muted);
        }
    }

    private void Act(string action,string? origin=null)
    {
        if(!Visible || _motion.Closing || !DashboardActions.TryParse(action,out var command)) return;
        if(origin!=null && _parts.TryGetValue(origin,out var pressed))
        {
            if(!pressed.Node.Enabled || pressed.Node.Content && !_motion.ContentInteractive) return;
            pressed.Pulse=1;
        }
        try
        {
            var nextTab=_pendingRoute?_pendingTab:_tab;
            int nextPage=_pendingRoute?_pendingPage:_page;
            switch(command)
            {
                case DashboardCommand.Close: RequestClose(); return;
                case DashboardCommand.Overview: nextTab=DashboardTab.Overview;nextPage=0; break;
                case DashboardCommand.Machines: nextTab=DashboardTab.Machines;nextPage=0; break;
                case DashboardCommand.Rules: nextTab=DashboardTab.Rules;nextPage=0; break;
                case DashboardCommand.PreviousPage: nextPage=Math.Max(0,nextPage-1); break;
                case DashboardCommand.NextPage: nextPage=Math.Min(DashboardLayout.PageCount(_machineCount)-1,nextPage+1); break;
                case DashboardCommand.ToggleTransport: mod.ToggleTransport();RefreshData();return;
            }
            if(nextTab==_tab && nextPage==_page && !_pendingRoute) return;
            _pendingTab=nextTab;_pendingPage=nextPage;_pendingRoute=true;
            _motion.RequestPage(mod.Settings.ReduceDashboardMotion);
        }
        catch(Exception error) { Fault(error); }
    }

    internal void Close()
    {
        try { CloseCore(); }
        catch(Exception error)
        {
            LastError=error.Message;
            mod.Diagnostics.Write(DiagnosticLevel.Warn,"dashboard_close_failed","面板关闭时原版界面已变化。",new{error=error.Message});
        }
    }

    private void CloseCore()
    {
        Visible=false; _pendingRoute=false;
        try { if(_canvas!=null) _canvas!.SetActive(false); }
        finally
        {
            _openedModal=false; _menu=null;
            _lease.Release();
        }
    }

    internal void RefreshSoon() => _nextRefresh=0;
    internal void TransportChanged(bool enabled)
    {
        RefreshSoon(); _feedback=enabled?"量子传送已启用 · 配置已保存":"量子传送已关闭 · 配置已保存";
        _feedbackUntil=Environment.TickCount64+1900;
    }

    private void Fault(Exception error)
    {
        LastError=error.Message;
        try { Close(); }
        catch(Exception closeError) { LastError+="; "+closeError.Message; }
        mod.Diagnostics.Write(DiagnosticLevel.Warn,"dashboard_unavailable","状态面板已关闭；运输调度独立运行。",new{error=error.ToString()});
    }

    public void Dispose()
    {
        try { Close(); }
        catch(Exception error) { mod.Log.Warn("状态面板关闭失败："+error.Message); }
        if(_canvas!=null) UObject.Destroy(_canvas!);
        _kit?.Dispose(); _kit=null; _canvas=null; _frame=null;_content=null;_canvasGroup=null;_frameGroup=null;_contentGroup=null; _parts.Clear();
    }
}
