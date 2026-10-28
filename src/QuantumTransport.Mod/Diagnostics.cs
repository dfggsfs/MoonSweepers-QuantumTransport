using System.Text;
using System.Text.Json;
using QuantumTransport.Core;
using Il2CppMoonCleaners;

namespace QuantumTransport;

internal sealed class Diagnostics(QuantumMod mod)
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private readonly string _session = Guid.NewGuid().ToString("N")[..12];
    private bool _reportedIoError;
    private long _detailSecond;
    private int _detailCount;
    private long _suppressed;
    private long _lastStateLog;
    public string State { get; set; } = "starting";
    public bool HookVerified { get; set; }
    public string? LastError { get; set; }
    public string DeliveryState { get; set; } = "waiting_for_world";
    internal ProductionAudit? Production { get; set; }
    private string DirectoryPath => Path.Combine(mod.Info.DataDirectory, "diagnostics");

    public void SetState(string state)
    {
        if (State == state) return;
        State = state;
        long now = Environment.TickCount64;
        if (now - _lastStateLog < 2000) return;
        _lastStateLog = now;
        Write(DiagnosticLevel.Info, "state", "状态：" + state, mod.Manager.LastStats);
        Snapshot();
    }

    public void Transfer(object data)
    {
        if (mod.Settings.LogLevel < DiagnosticLevel.Debug) return;
        long second = Environment.TickCount64 / 1000;
        if (second != _detailSecond) { _detailSecond = second; _detailCount = 0; }
        if (_detailCount++ >= 20) { _suppressed++; return; }
        Write(DiagnosticLevel.Debug, "transfer", "资源转运提交成功。", data);
    }

    internal void CaptureProduction(MoonWorld world)
    {
        try
        {
            Production = ProductionAudit.Read(world, Production);
        }
        catch (Exception error)
        {
            Write(DiagnosticLevel.Warn, "production_probe_failed", "生产状态读取失败。", new { error = error.Message });
        }
    }

    public void Write(DiagnosticLevel level, string code, string message, object? data = null)
    {
        if (mod.Settings.LogLevel == DiagnosticLevel.Off || level > mod.Settings.LogLevel) return;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            string path = Path.Combine(DirectoryPath, "quantum.jsonl");
            if (File.Exists(path) && new FileInfo(path).Length >= MaxBytes)
            {
                string older = Path.Combine(DirectoryPath, "quantum.2.jsonl");
                string previous = Path.Combine(DirectoryPath, "quantum.1.jsonl");
                if (File.Exists(previous)) File.Move(previous, older, true);
                File.Move(path, previous, true);
            }
            string line = JsonSerializer.Serialize(new { utc=DateTimeOffset.UtcNow, session=_session, level=level.ToString(), code, message, data });
            File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
        }
        catch (Exception error) { ReportIoError(error); }
        if (level == DiagnosticLevel.Error) mod.Log.Error(code + ": " + message + (data == null ? "" : " " + JsonSerializer.Serialize(data)));
        else if (level == DiagnosticLevel.Warn) mod.Log.Warn(code + ": " + message);
        else if (level == DiagnosticLevel.Info && code != "summary") mod.Log.Info(code + ": " + message);
    }

    public void Snapshot()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            string path = Path.Combine(DirectoryPath, "status.json");
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new
            {
                utc=DateTimeOffset.UtcNow, session=_session, modVersion=mod.Info.Version, state=State,
                enabled=mod.Settings.Enabled, logLevel=mod.Settings.LogLevel.ToString(), hookVerified=HookVerified,
                routing=new { distribution=mod.Settings.DustDistribution.ToString(), mod.Settings.ChamberDustPriority,
                    mod.Settings.PressDustPriority, mod.Settings.PressDustBufferKilograms, mod.Settings.PackagerDustReserveKilograms,
                    mod.Settings.CollectPlayerDust, mod.Settings.CollectVehicleDust, mod.Settings.CollectRobotDust, mod.Settings.CollectGarageDust, mod.Settings.DeliverStone },
                dashboard=new { mod.Settings.EnableDashboard, visible=mod.Dashboard.Visible, error=mod.Dashboard.LastError },
                deliveryState=DeliveryState,
                network=new {role=mod.Coop.Role.ToString(),mod.Coop.InFlight,mod.Coop.PendingPackets,mod.Coop.StagedObjects,mod.Coop.AcceptedObjects,mod.Coop.DuplicatePackets,
                    mod.Coop.RejectedPackets,mod.Coop.LastRejection,mod.Coop.SendFailures,
                    hostReady=mod.Coop.HostAdvertisement?.Enabled??false},
                scheduler=mod.Manager.Performance,
                stats=mod.Manager.LastStats, production=Production, suppressedDetailLines=_suppressed, lastError=LastError
            }, new JsonSerializerOptions { WriteIndented=true }), new UTF8Encoding(false));
            File.Move(temp, path, true);
        }
        catch (Exception error) { ReportIoError(error); }
    }

    private void ReportIoError(Exception error)
    {
        if (_reportedIoError) return;
        _reportedIoError = true;
        mod.Log.Warn("量子运输诊断文件暂时无法写入：" + error.Message);
    }
}
