using System.ComponentModel;
using Il2CppMoonCleaners;
using Lustral;
using QuantumTransport.Core;
using HarmonyLib;

namespace QuantumTransport;

public sealed class Settings
{
    [Description("开启采集库存、机器供料与月石交付的全图量子运输；Ctrl+F8 可在游戏内切换。")]
    public bool Enabled { get; set; } = true;
    [Description("日志级别：Off、Error、Warn、Info、Debug；Ctrl+F9 切换 Info/Debug。Debug 最多每秒记录20条转运。")]
    public DiagnosticLevel LogLevel { get; set; } = DiagnosticLevel.Info;
    [Description("汇总日志间隔（秒），范围2至300。")]
    public int SummaryIntervalSeconds { get; set; } = 15;
    [Description("调度间隔（毫秒），范围16至1000。距离不会影响转运时间。")]
    public int PollIntervalMilliseconds { get; set; } = 100;
    [Description("月尘分配：RecipeBalanced 按压石机现有月砂加预备量供料；CapacityFair 按全部输入容量供料。")]
    public DustDistribution DustDistribution { get; set; } = DustDistribution.RecipeBalanced;
    [Description("加压舱月尘优先级，-100至100，数值越大越先供料；同级公平分配。默认与压石机同级。")]
    public int ChamberDustPriority { get; set; }
    [Description("压石机月尘优先级，-100至100；优先级不绕过配方需求限制。持续优先供料可能延迟低优先级机器。")]
    public int PressDustPriority { get; set; }
    [Description("RecipeBalanced 模式下，每台压石机在现有月砂对应的月尘外额外预备多少公斤，默认1；范围0至12。只限制新转入，不取走原库存。")]
    public double PressDustBufferKilograms { get; set; } = 1;
    [Description("每台打包机为量子运输保留的月尘总量（跨所有归属合计），默认0；范围0至1000000。原版传送带仍可取走这些资源。")]
    public double PackagerDustReserveKilograms { get; set; }
    [Description("将玩家当前及已存放容器中的已入账月尘直接供给兼容加工机；无接收空间时保留。")]
    public bool CollectPlayerDust { get; set; } = true;
    [Description("将车辆真实料斗库存直接供给兼容加工机，保留车辆的原归属。")]
    public bool CollectVehicleDust { get; set; } = true;
    [Description("将已卸进车库储仓的月尘按原玩家归属直接供给加工机；联机由主机处理，无需传送带。")]
    public bool CollectGarageDust { get; set; } = true;
    [Description("接入近处机器人与远处机器人已完成采集并入账的库存，无需等待回站卸货。")]
    public bool CollectRobotDust { get; set; } = true;
    [Description("将压石机成品月石自动交付原版收集站，遵守站点容量、贡献与溢价记录；发射和付款仍由原版流程处理。")]
    public bool DeliverStone { get; set; } = true;
    [Description("启用联机：主机统一分配与交付；客户端需同版本才能自动接入个人采集，使用有回执的原生资源对象交接。")]
    public bool EnableCoop { get; set; } = true;
    [Description("接入未被拿起、未飞行、未在传送带上的资源胶囊。用于联机交接和原版手工打包物品。")]
    public bool CollectLooseCapsules { get; set; } = true;
    [Description("每轮最多检查的来源数，16至4096；按轮询顺序防止后面的机器饿死。")]
    public int MaxSourcesPerTick { get; set; } = 128;
    [Description("每轮最多刷新接收口库存数，16至4096；实际提交前始终重新核对库存与需求。")]
    public int MaxReceiverSamplesPerTick { get; set; } = 128;
    [Description("每轮最多完成的库存事务数，16至4096。")]
    public int MaxTransfersPerTick { get; set; } = 128;
    [Description("热调度工作预算，1至20毫秒；元数据索引重建单独计时，不承诺整个游戏帧时间。")]
    public double TickBudgetMilliseconds { get; set; } = 4;
    [Description("来源索引低频兜底刷新间隔，250至10000毫秒；结构及集合数量变化会提前刷新。")]
    public int CatalogRefreshMilliseconds { get; set; } = 1000;
    [Description("主机最短同步提醒间隔，100至2000毫秒；实际传输沿用原版去重与节流。")]
    public int NetworkSyncMilliseconds { get; set; } = 250;
    [Description("每个客户端尚未被主机消耗的交接对象上限，1至32；满载时停止新的交接。")]
    public int MaxClientCapsulesInFlight { get; set; } = 8;
    [Description("每轮客户端最多创建的交接对象，1至4。")]
    public int ClientCapsulesPerTick { get; set; } = 1;
    [Description("仅供手动诊断：启动后检查独立的原生库存对象，不创建机器或修改世界库存。默认关闭。")]
    public bool RunNativeChecks { get; set; }
    [Description("启用游戏内运输面板，Ctrl+F7 打开或关闭；可查看状态并切换运输启用状态，不修改配方分配规则。")]
    public bool EnableDashboard { get; set; } = true;
    [Description("减少面板动态效果：跳过开合、切页、按钮缩放和颜色渐变，保留清晰的悬停及选中反馈。")]
    public bool ReduceDashboardMotion { get; set; }

    internal void Normalize()
    {
        SummaryIntervalSeconds = Math.Clamp(SummaryIntervalSeconds, 2, 300);
        PollIntervalMilliseconds = Math.Clamp(PollIntervalMilliseconds, 16, 1000);
        if (!Enum.IsDefined(LogLevel)) LogLevel = DiagnosticLevel.Info;
        if (!Enum.IsDefined(DustDistribution)) DustDistribution = DustDistribution.RecipeBalanced;
        ChamberDustPriority = Math.Clamp(ChamberDustPriority, -100, 100);
        PressDustPriority = Math.Clamp(PressDustPriority, -100, 100);
        PressDustBufferKilograms = double.IsFinite(PressDustBufferKilograms) ? Math.Clamp(PressDustBufferKilograms, 0, 12) : 1;
        PackagerDustReserveKilograms = double.IsFinite(PackagerDustReserveKilograms) ? Math.Clamp(PackagerDustReserveKilograms, 0, 1_000_000) : 0;
        MaxSourcesPerTick=Math.Clamp(MaxSourcesPerTick,16,4096);
        MaxReceiverSamplesPerTick=Math.Clamp(MaxReceiverSamplesPerTick,16,4096);
        MaxTransfersPerTick=Math.Clamp(MaxTransfersPerTick,16,4096);
        TickBudgetMilliseconds=double.IsFinite(TickBudgetMilliseconds)?Math.Clamp(TickBudgetMilliseconds,1,20):4;
        CatalogRefreshMilliseconds=Math.Clamp(CatalogRefreshMilliseconds,250,10000);
        NetworkSyncMilliseconds=Math.Clamp(NetworkSyncMilliseconds,100,2000);
        MaxClientCapsulesInFlight=Math.Clamp(MaxClientCapsulesInFlight,1,32);
        ClientCapsulesPerTick=Math.Clamp(ClientCapsulesPerTick,1,4);
    }
}

public sealed class QuantumMod : Mod
{
    internal static QuantumMod Instance { get; private set; } = null!;
    internal Settings Settings { get; private set; } = new();
    internal TransportManager Manager { get; private set; } = null!;
    internal Diagnostics Diagnostics { get; private set; } = null!;
    internal CoopCoordinator Coop { get; private set; } = null!;
    internal Dashboard Dashboard { get; private set; } = null!;
    internal MoonWorld? CurrentWorld => _world;
    private MoonWorld? _world;
    private long _lastWorldSearch;
    private long _lastProbe;
    private bool _failed;
    private long _lastPoll;
    private long _lastSummary;

    public QuantumMod() => Instance = this;

    public override void OnLoad()
    {
        Settings = Config.Bind<Settings>();
        Settings.Normalize();
        Diagnostics = new Diagnostics(this);
        Manager = new TransportManager(this);
        Coop = new CoopCoordinator(this);
        Dashboard = new Dashboard(this);
        Diagnostics.Write(DiagnosticLevel.Info, "routing_policy", "供料规则已加载。", new { Settings.DustDistribution, Settings.ChamberDustPriority,
            Settings.PressDustPriority, Settings.PressDustBufferKilograms, Settings.PackagerDustReserveKilograms,
            Settings.CollectPlayerDust, Settings.CollectVehicleDust, Settings.CollectRobotDust, Settings.CollectGarageDust, Settings.DeliverStone });
        Diagnostics.Write(DiagnosticLevel.Info, "startup", "Quantum Transport：采集月尘、加工供料与月石交付。", new { modVersion=Info.Version, gameVersion=Game.Version, unityVersion=Game.UnityVersion?.ToString() });
        if (!BuildCompatibility.IsSupported(Game.Directory, out string reason))
        {
            _failed = true;
            Diagnostics.State = "unsupported_build";
            Diagnostics.Write(DiagnosticLevel.Error, "unsupported_build", reason);
            Diagnostics.Snapshot();
            return;
        }
        // Do not detour StepConveyors(float): the installed interop trampoline maps
        // Single to a byte struct, which uses a different native Windows x64 ABI.
        Harmony.PatchAll(typeof(QuantumMod).Assembly);
        Events.LateUpdate += UpdateWorld;
        Events.SceneLoaded += _ => { Dashboard.Close(); _world = null; Manager.ResetSources(); };
        Events.SceneUnloaded += _ => { Dashboard.Close(); _world = null; Manager.ResetSources(); };
        Events.Update += Controls;
        Events.Update += Dashboard.Update;
        Events.Update += Summary;
        Diagnostics.State = Settings.Enabled ? "waiting_for_world" : "disabled";
        Diagnostics.Snapshot();
    }

    public override void OnGameReady()
    {
        if (_failed) return;
        Diagnostics.Write(DiagnosticLevel.Info, "game_ready", "已进入游戏界面，等待帧末调度发现实际世界。原版生产函数不作拦截。");
        if (!Settings.RunNativeChecks) return;
        try { NativeChecks.Run(this); }
        catch (Exception error) { Fail(error, "native_checks"); }
    }

    private void Controls()
    {
        if (_failed) return;
        try
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null || !(bool)keyboard.ctrlKey.isPressed) return;
            if ((bool)keyboard.f7Key.wasPressedThisFrame) Dashboard.Toggle();
            if ((bool)keyboard.f8Key.wasPressedThisFrame)
            {
                ToggleTransport();
            }
            if ((bool)keyboard.f9Key.wasPressedThisFrame)
            {
                Settings.LogLevel = Settings.LogLevel == DiagnosticLevel.Debug ? DiagnosticLevel.Info : DiagnosticLevel.Debug;
                Config.Save(Settings);
                Diagnostics.Write(DiagnosticLevel.Info, "log_level", $"日志级别：{Settings.LogLevel}");
                Diagnostics.Snapshot();
            }
            if ((bool)keyboard.f10Key.wasPressedThisFrame)
            {
                Diagnostics.Snapshot();
                Diagnostics.Write(DiagnosticLevel.Info, "manual_snapshot", "已写出 status.json。", Manager.LastStats);
            }
        }
        catch (Exception error)
        {
            Events.Update -= Controls;
            Diagnostics.Write(DiagnosticLevel.Warn, "controls_unavailable", "快捷键不可用；配置文件仍可使用，修改后重启游戏。", new { error=error.ToString() });
        }
    }

    internal void ToggleTransport()
    {
        if(_failed)
        {
            Diagnostics.Write(DiagnosticLevel.Warn,"toggle_unavailable","本次会话存在错误，修复后重启游戏再启用运输。");
            return;
        }
        bool before=Settings.Enabled;
        Settings.Enabled=!before;
        try { Config.Save(Settings); }
        catch { Settings.Enabled=before; throw; }
        Diagnostics.State=Settings.Enabled ? (_world==null?"waiting_for_world":"waiting_for_supply") : "disabled";
        Diagnostics.Write(DiagnosticLevel.Info,"toggle",Settings.Enabled?"量子运输已开启。":"量子运输已关闭。");
        Dashboard.TransportChanged(Settings.Enabled);
        Diagnostics.Snapshot();
    }

    private void Summary()
    {
        long now = Environment.TickCount64;
        if (now - _lastSummary < Settings.SummaryIntervalSeconds * 1000L) return;
        _lastSummary = now;
        Diagnostics.Write(DiagnosticLevel.Info, "summary", "量子运输与原版生产状态汇总。", new { stats=Manager.LastStats, scheduler=Manager.Performance,
            network=new {role=Coop.Role.ToString(),Coop.InFlight,Coop.PendingPackets,Coop.StagedObjects,Coop.AcceptedObjects,Coop.DuplicatePackets,
                Coop.RejectedPackets,Coop.LastRejection,Coop.SendFailures},production=Diagnostics.Production });
        Diagnostics.Snapshot();
    }

    private void UpdateWorld()
    {
        if (_failed) return;
        try
        {
            if (!(bool)MoonSession.InGame)
            {
                if (_world != null) Manager.ResetSources();
                Dashboard.Close();
                _world = null;
                Diagnostics.Production = null;
                Diagnostics.SetState(Settings.Enabled ? "waiting_for_world" : "disabled");
                return;
            }
            long now = Environment.TickCount64;
            if (_world == null)
            {
                if (now - _lastWorldSearch < 1000) return;
                _lastWorldSearch = now;
                _world = UnityEngine.Object.FindFirstObjectByType<MoonWorld>();
                if (_world == null) return;
                Diagnostics.HookVerified = true;
                Diagnostics.Production = null;
                Diagnostics.Write(DiagnosticLevel.Info, "scheduler_verified", "帧末调度已发现 MoonWorld；原版物流和生产函数保持原始调用。");
            }
            if (now - _lastProbe >= 1000)
            {
                _lastProbe = now;
                Diagnostics.CaptureProduction(_world!);
            }
            Tick(_world!);
        }
        catch (Exception error) { Fail(error, "world_update_exception"); }
    }

    internal void Tick(MoonWorld world)
    {
        if (_failed) return;
        try
        {
            Coop.Update(world);
            if (!Settings.Enabled) { Diagnostics.DeliveryState = "disabled"; Diagnostics.SetState("disabled"); return; }
            if (Coop.Role==TransportRole.Waiting || (!Settings.EnableCoop && Coop.Role!=TransportRole.SinglePlayer))
            { Diagnostics.DeliveryState = "co_op_disabled"; Diagnostics.SetState("co_op_disabled"); return; }
            long now = Environment.TickCount64;
            if (now - _lastPoll < Settings.PollIntervalMilliseconds) return;
            _lastPoll = now;
            if (Coop.Role==TransportRole.Client) Coop.TickClient(world);
            else Manager.Tick(world);
        }
        catch (Exception error) { Fail(error, "transfer_exception"); }
    }

    private void Fail(Exception error, string code)
    {
        _failed = true;
        Diagnostics.State = "error_disabled";
        Diagnostics.LastError = error.ToString();
        Diagnostics.Write(DiagnosticLevel.Error, code, "本次会话已停用量子运输，原游戏生产继续运行。", new { error=error.ToString() });
        Diagnostics.Snapshot();
    }
    internal void ReceiveNetwork(MoonCrewSession session,ulong sender,byte[] bytes)
    {
        if(_failed) return;
        try { Coop.Receive(session,sender,bytes); }
        catch(Exception error) { Fail(error,"network_handoff_exception"); }
    }

    public override void OnShutdown()
    {
        Dashboard?.Dispose();
        try { Coop?.ClearAdvertisement(); }
        catch(Exception error) { Diagnostics?.Write(DiagnosticLevel.Warn,"lobby_cleanup_failed","退出时大厅状态清理未完成。",new{error=error.Message}); }
        Diagnostics?.Write(DiagnosticLevel.Info, "shutdown", "量子运输结束。", Manager?.LastStats);
        Diagnostics?.Snapshot();
    }
}
