using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using QuantumTransport.Core;

string game = @"C:\Program Files (x86)\Steam\steamapps\common\Moon Sweepers";
string? interop = null;
while (args.Length >= 2 && args[0] is "--game" or "--interop")
{
    if (args[0] == "--game") game = Path.GetFullPath(args[1]);
    else interop = Path.GetFullPath(args[1]);
    args = args.Skip(2).ToArray();
}
string[] dirs = [interop ?? Path.Combine(game, "Lustral/interop"), Path.Combine(game, "Lustral/core")];
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    foreach (string dir in dirs)
    {
        string path = Path.Combine(dir, name.Name + ".dll");
        if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    }
    return null;
};
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(dirs[0], "Assembly-CSharp.dll"));
if (args.Length > 1 && args[0] == "--assembly")
{
    assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(dirs[0], args[1]));
    args = args.Skip(2).ToArray();
}
if (args.Length > 0 && args[0] == "--verify")
{
    var results = new List<object>();
    int failures = 0;
    void Check(bool passed, string label)
    {
        results.Add(new { label, passed });
        if (!passed) failures++;
    }
    Check(BuildCompatibility.IsSupported(game, out string reason), "supported game fingerprint: " + reason);
    using var compatibility = JsonDocument.Parse(File.ReadAllText("distribution/game-compatibility.json"));
    var profile = compatibility.RootElement;
    Check(BuildCompatibility.ExpectedSteamBuild == profile.GetProperty("steamBuildId").GetString() &&
        profile.GetProperty("gameFiles").EnumerateArray().All(f => f.GetProperty("sha256").GetString() ==
            (f.GetProperty("path").GetString() == "GameAssembly.dll" ? BuildCompatibility.ExpectedAssemblyHash : BuildCompatibility.ExpectedMetadataHash)),
        "embedded runtime fingerprints match the shared installer/diagnostic profile");
    using var interopManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(dirs[0], "interop.json")));
    Check(profile.GetProperty("interop").EnumerateObject().All(p => p.Value.ToString() == interopManifest.RootElement.GetProperty(p.Name).ToString()) &&
        interopManifest.RootElement.GetProperty("Unstripped").GetBoolean(), "inspected interfaces were generated for this exact build and include restored Unity APIs");
    using (var metadata = new BinaryReader(File.OpenRead(Path.Combine(game, "MoonSweepers_Data/il2cpp_data/Metadata/global-metadata.dat"))))
        Check(metadata.ReadUInt32() == 0xFAB11BAF && metadata.ReadInt32() == BuildCompatibility.ExpectedMetadataVersion,
            "actual metadata header matches the supported format");
    var world = assembly.GetType("Il2CppMoonCleaners.MoonWorld", true)!;
    var state = assembly.GetType("Il2CppMoonCleaners.MoonProductionState", true)!;
    var snapshot = assembly.GetType("Il2CppMoonCleaners.MoonVehicleStorageSnapshot", true)!;
    var api=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(dirs[1], "Lustral.Api.dll"));
    Check(api.GetType("Lustral.GameEvents")?.GetEvent("LateUpdate")!=null, "loader LateUpdate scheduler event exists");
    Check(assembly.GetType("Il2CppMoonCleaners.MoonSession")?.GetProperty("InGame")!=null, "world session readiness API");
    var unity=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(dirs[0], "UnityEngine.CoreModule.dll"));
    Check(unity.GetType("UnityEngine.Object")?.GetMethods().Any(m=>m.Name=="FindFirstObjectByType" && m.IsGenericMethod)==true, "Unity world discovery API");
    Check(unity.GetType("UnityEngine.Object")?.GetMethods().Any(m=>m.Name=="FindObjectsByType" && m.IsGenericMethod)==true, "Unity prototype vehicle discovery API");
    foreach (string name in new[] {"Put", "InputSlot", "PackagerStoragePrefix", "GarageSiloPrefix", "NewProduction", "RobotStorageId", "DockStorageId", "MachineStorage", "IsRobotDock"})
        Check(world.GetMethods().Any(m=>m.Name==name && m.IsStatic), "native static method " + name);
    foreach (string name in new[] {"ProductionOf", "RequestSave", "CrewCargoDeposit", "LoadedContainerId", "IsGarage"})
        Check(world.GetMethods().Any(m=>m.Name==name && !m.IsStatic), "native instance method " + name);
    foreach (string name in new[] {"depots", "production", "vehicleStorage", "packagerStatus", "PendingSave", "productionNow", "progressDirty",
        "inventoryMass", "toolMass", "ownedVehicles", "garageVehicles", "farRobots", "collector", "Stations", "pendingContributions", "premiums", "depositedKilograms", "StoredStationKilograms"})
        Check(world.GetProperty(name) != null, "world property " + name);
    foreach (string name in new[] {"Stored", "Capacity"})
        Check(state.GetMethods().Any(m=>m.Name==name), "processor method " + name);
    foreach (string name in new[] {"stores", "lastInput", "Kind", "Running", "Progress", "Power", "DemandKw", "UsedKw", "Cycles"})
        Check(state.GetProperty(name)?.CanRead == true, "processor property " + name);
    Check(snapshot.GetProperty("kilograms")?.CanWrite==true, "source kilograms writable");
    foreach (string name in new[] {"ChamberSandOut","ChamberDustIn","PressureChamberKind","StonePressKind","PressSandIn", "PressBatchDust", "PressBatchSand", "PressStoneOut"})
        Check(world.GetField(name,BindingFlags.Public|BindingFlags.Static)!=null,"native resource/slot constant " + name);
    var input = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(dirs[0], "Unity.InputSystem.dll"));
    var keyboard = input.GetType("UnityEngine.InputSystem.Keyboard", true)!;
    foreach (string name in new[] {"current", "ctrlKey", "escapeKey", "pageUpKey", "pageDownKey", "enterKey", "spaceKey", "f7Key", "f8Key", "f9Key", "f10Key"})
        Check(keyboard.GetProperty(name)!=null, "input API " + name);
    var mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[1]));
    var modMethods=mod.GetTypes().SelectMany(t=>t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly)
        .Cast<MethodBase>().Concat(t.GetConstructors(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance))).ToArray();
    var modCalls=modMethods.SelectMany(IlCalls.Read).ToArray();
    Check(!modCalls.Any(m=>m.DeclaringType?.FullName=="Il2CppMoonCleaners.MoonPauseMenu" &&
        m.Name is "CloseWindow" or "RequestQuit" or "RequestMainMenu" or "LeaveAfterSave"),"regression: delivered mod IL contains no pause-menu quit or leave-world calls");
    Check(!modCalls.Any(m=>m.DeclaringType?.FullName=="UnityEngine.Application" && m.Name=="Quit"),"regression: delivered mod IL never calls Application.Quit");
    Check(modCalls.Any(m=>m.DeclaringType?.FullName=="Il2CppMoonCleaners.MoonPauseMenu" && m.Name=="Resume"),"dashboard cleanup uses verified Resume API");
    foreach(string methodName in new[]{"Controls"})
        Check(IlCalls.Read(mod.GetType("QuantumTransport.QuantumMod",true)!.GetMethod(methodName,BindingFlags.NonPublic|BindingFlags.Instance)!).Any(m=>m.Name=="ToggleTransport"),"keyboard control uses shared persisted transport switch");
    Check(IlCalls.Read(mod.GetType("QuantumTransport.Dashboard",true)!.GetMethod("Act",BindingFlags.NonPublic|BindingFlags.Instance)!).Any(m=>m.Name=="ToggleTransport"),"UI button uses same transport switch as Ctrl+F8");
    Check(modCalls.Any(m=>m.DeclaringType?.FullName=="Il2CppMoonCleaners.MoonSimpleDialog" && m.Name=="set_onAccept"),"native underlying modal submit callback explicitly isolated");
    var settingsType=mod.GetType("QuantumTransport.Settings", true)!;
    var settings=Activator.CreateInstance(settingsType)!;
    Check((bool)settingsType.GetProperty("Enabled")!.GetValue(settings)!, "default transport enabled");
    Check(!(bool)settingsType.GetProperty("RunNativeChecks")!.GetValue(settings)!, "default native runtime checks disabled");
    Check(settingsType.GetProperty("DustDistribution")!.GetValue(settings)!.ToString()=="RecipeBalanced", "default recipe-aware dust distribution");
    Check((double)settingsType.GetProperty("PressDustBufferKilograms")!.GetValue(settings)! == 1, "default sandless press dust buffer is one kilogram");
    Check((double)settingsType.GetProperty("PackagerDustReserveKilograms")!.GetValue(settings)! == 0, "default packager reserve is zero");
    Check((int)settingsType.GetProperty("ChamberDustPriority")!.GetValue(settings)! == (int)settingsType.GetProperty("PressDustPriority")!.GetValue(settings)!, "default equal dust priorities");
    foreach (string name in new[] {"CollectPlayerDust", "CollectVehicleDust", "CollectRobotDust", "CollectGarageDust", "EnableDashboard", "DeliverStone"})
        Check((bool)settingsType.GetProperty(name)!.GetValue(settings)!, "default enabled " + name);
    foreach (var contract in new[] {
        (Type:"MoonOwnedVehicle", Names:new[]{"Storage", "owner"}),
        (Type:"MoonPrototypeVehicle", Names:new[]{"StorageId", "owner"}),
        (Type:"MoonPrototypeEquipment", Names:new[]{"Player"}),
        (Type:"MoonWalker", Names:new[]{"UpgradePlayer"}),
        (Type:"MoonRobotDock", Names:new[]{"All", "World", "DockId", "Owner", "StorageId"}) })
        foreach (string name in contract.Names)
            Check(assembly.GetType("Il2CppMoonCleaners." + contract.Type)!.GetProperty(name)!=null, "collection identity API " + contract.Type + "." + name);
    Check(assembly.GetType("Il2CppMoonCleaners.MoonWorld+FarRobot")?.GetProperty("kilograms")?.CanWrite==true,
        "far robot confirmed payload is writable independently of work/need progress");
    Check(mod.GetType("QuantumTransport.CollectionSources")!=null && mod.GetType("QuantumTransport.StoneDelivery")!=null,
        "collection adapters and native station delivery present");
    settingsType.GetProperty("PressDustBufferKilograms")!.SetValue(settings, double.NaN);
    settingsType.GetProperty("PackagerDustReserveKilograms")!.SetValue(settings, double.PositiveInfinity);
    settingsType.GetProperty("ChamberDustPriority")!.SetValue(settings, 1000);
    settingsType.GetMethod("Normalize", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(settings, null);
    Check((double)settingsType.GetProperty("PressDustBufferKilograms")!.GetValue(settings)! == 1 &&
        (double)settingsType.GetProperty("PackagerDustReserveKilograms")!.GetValue(settings)! == 0 &&
        (int)settingsType.GetProperty("ChamberDustPriority")!.GetValue(settings)! == 100, "invalid numeric settings normalized safely");
    Check(mod.GetType("QuantumTransport.IntegrationChecks")==null, "automatic world fixture absent from delivered assembly");
    Check(mod.GetType("QuantumTransport.IntegrationSavePatch")==null, "save override patch absent from delivered assembly");
    Check(mod.GetType("QuantumTransport.WorldStepPatch")==null, "regression: native StepConveyors detour removed");
    var patchTypes=mod.GetTypes().Where(t=>t.GetCustomAttributesData().Any(a=>a.AttributeType.FullName=="HarmonyLib.HarmonyPatch")).ToArray();
    Check(patchTypes.Length==1 && patchTypes[0].FullName=="QuantumTransport.NetworkPatch", "only the integer/pointer network hook exists; no production/float hook");
    var target=patchTypes.Single().GetMethod("TargetMethod",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,null) as MethodBase;
    Check(target?.Name=="HandleWorld" && target.DeclaringType?.Name=="MoonCrewSession" &&
        target.GetParameters()[0].ParameterType.FullName=="Il2CppSystem.UInt64" &&
        target.GetParameters()[1].ParameterType.GetGenericTypeDefinition().FullName=="Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayRank1`1" &&
        target.GetParameters()[1].ParameterType.GetGenericArguments()[0].FullName=="Il2CppSystem.Byte",
        "network hook resolves the native uint64/byte-array overload");
    var crew=assembly.GetType("Il2CppMoonCleaners.MoonCrewSession",true)!;
    foreach(string name in new[]{"Instance","bound","local","Joining","idBlocks","nextMachineStorage","nextProduction","walletDirty"})
        Check(crew.GetProperty(name)!=null,"co-op native session API "+name);
    foreach(string name in new[]{"PackDustPortion","ApplyCrewCanister","RemoveCanister","RaiseMachineCanister","IsCanisterCarried","IsCanisterFlying","IsCanisterRiding","WriteCrewPlayerState","WriteCrewVehicleReport","ApplyCrewPlayerState","ApplyCrewVehicleReport"})
        Check(world.GetMethods().Any(m=>m.Name==name),"co-op native cargo/source API "+name);
    foreach(string name in new[]{"EnableCoop","CollectLooseCapsules"})Check((bool)settingsType.GetProperty(name)!.GetValue(settings)!,"default enabled "+name);
    foreach(string name in new[]{"MaxSourcesPerTick","MaxReceiverSamplesPerTick","MaxTransfersPerTick"})Check((int)settingsType.GetProperty(name)!.GetValue(settings)! == 128,"bounded default "+name);
    var nativeWord=assembly.GetType("Il2CppMoonCleaners.MoonCrew")!.GetProperty("HostId")!.PropertyType;
    Check(System.Runtime.InteropServices.Marshal.SizeOf(nativeWord)==8,"uint64 network hook wrapper has eight-byte integer ABI layout");
    var pack=world.GetMethods().Single(m=>m.Name=="PackDustPortion" && m.GetParameters()[0].ParameterType==typeof(string));
    Check(pack.GetParameters()[4].IsOut && pack.GetParameters()[4].ParameterType==typeof(int).MakeByRefType(),"native packing convenience overload returns out int ID");
    settingsType.GetProperty("TickBudgetMilliseconds")!.SetValue(settings,double.NaN);
    settingsType.GetProperty("MaxTransfersPerTick")!.SetValue(settings,int.MaxValue);
    settingsType.GetProperty("MaxClientCapsulesInFlight")!.SetValue(settings,-1);
    settingsType.GetMethod("Normalize", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(settings,null);
    Check((double)settingsType.GetProperty("TickBudgetMilliseconds")!.GetValue(settings)! == 4 &&
        (int)settingsType.GetProperty("MaxTransfersPerTick")!.GetValue(settings)! == 4096 &&
        (int)settingsType.GetProperty("MaxClientCapsulesInFlight")!.GetValue(settings)! == 1,"scale/network configuration bounds normalize safely");
    Check(mod.GetType("QuantumTransport.ProductionAudit")!=null, "production clock and machine audit present");
    Check(mod.GetType("QuantumTransport.Dashboard")!=null, "native in-game dashboard present independently of transport scheduler");
    Check(!modCalls.Any(m=>m.DeclaringType?.FullName=="Il2CppMoonCleaners.MoonMenuKit" && m.Name=="Button"),
        "dashboard no longer creates glass-shader buttons that conflict with panel palette");
    Check(modCalls.Any(m=>m.DeclaringType?.FullName=="UnityEngine.UI.Selectable" && m.Name=="set_transition"),
        "dashboard explicitly controls Selectable tint transition");
    Check(!(bool)settingsType.GetProperty("ReduceDashboardMotion")!.GetValue(settings)!,"default dashboard motion enabled with an accessible reduced-motion option");
    var kit=assembly.GetType("Il2CppMoonCleaners.MoonMenuKit",true)!;
    Check(kit.GetConstructors().Any(c=>c.GetParameters().Length==1 && c.GetParameters()[0].ParameterType.FullName=="Il2CppSystem.String"), "native UI kit named font/material constructor exists");
    foreach(string name in new[]{"Canvas","Rect","Panel","Label","Button","Dispose"})
        Check(kit.GetMethods().Any(m=>m.Name==name),"native rounded panel/font UI API "+name);
    Check(kit.GetMethods().Any(m=>m.Name=="Button" && m.GetParameters().Any(p=>p.ParameterType==typeof(Action))),"native UI button has managed delegate convenience overload");
    var pause=assembly.GetType("Il2CppMoonCleaners.MoonPauseMenu",true)!;
    foreach(string name in new[]{"Open","Resume","ShowModal","HideModal"})
        Check(pause.GetMethods().Any(m=>m.Name==name),"original gameplay/cursor/modal lifecycle "+name);
    foreach(string name in new[]{"IsOpen","ModalOpen","IsSaving","DisplayConfirmationPending"})
        Check(pause.GetProperty(name)!=null,"original menu safety state "+name);
    Check(assembly.GetType("Il2CppMoonCleaners.MoonWalker")!.GetProperty("PauseMenu")!=null,"local walker menu binding");
    Check(pause.GetProperty("dialog")!=null && assembly.GetType("Il2CppMoonCleaners.MoonSimpleDialog")!.GetProperty("onAccept")?.CanWrite==true,
        "owned native modal submit handler is writable independently of menu/game lifetime");
    Check(assembly.GetType("Il2CppMoonCleaners.MoonSimpleDialog")!.GetProperty("onCancel")?.CanWrite==true &&
        pause.GetProperty("modalCancel")?.CanWrite==true && pause.GetProperty("modalAccept")?.CanWrite==true,
        "owned modal accept/cancel handlers can all be cleared while dashboard owns ESC");
    var ui=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(dirs[0],"UnityEngine.UI.dll"));
    var uiModule=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(dirs[0],"UnityEngine.UIModule.dll"));
    var tmp=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(dirs[0],"Unity.TextMeshPro.dll"));
    Check(ui.GetType("UnityEngine.UI.CanvasScaler")?.GetProperty("enabled")!=null,"responsive UI CanvasScaler lifecycle");
    foreach(string name in new[]{"alpha","interactable","blocksRaycasts"})
        Check(uiModule.GetType("UnityEngine.CanvasGroup")?.GetProperty(name)?.CanWrite==true,"independent frame/content animation CanvasGroup API "+name);
    Check(uiModule.GetType("UnityEngine.RectTransformUtility")?.GetMethods().Any(m=>m.Name=="RectangleContainsScreenPoint")==true,"scale-aware native button pointer hit test");
    Check(unity.GetType("UnityEngine.Events.UnityEvent")?.GetMethods().Any(m=>m.Name=="AddListener" && m.GetParameters().Any(p=>p.ParameterType==typeof(Action)))==true,
        "flat native UGUI button supports managed click delegate");
    foreach(string name in new[]{"text","fontSize","color","alignment","textWrappingMode","overflowMode","richText","raycastTarget"})
        Check(tmp.GetType("Il2CppTMPro.TMP_Text")!.GetProperty(name)!=null,"native Chinese text rendering API "+name);
    var core=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(dirs[1], "Lustral.Core.dll"));
    var manifest=core.GetType("Lustral.Core.Mods.ModManifest",true)!;
    try { manifest.GetMethod("Parse")!.Invoke(null,[File.ReadAllText(args[2])]); Check(true,"loader manifest parsed by actual loader parser"); }
    catch(Exception error) { Check(false,"manifest parser: " + error.GetBaseException().Message); }
    string temp = Path.Combine(Path.GetTempPath(), "quantum-offline-config-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    try
    {
        string file = Path.Combine(temp, "test.toml");
        File.WriteAllText(file, "enabled = false\nlog_level = \"Warn\"\nsummary_interval_seconds = 23\npoll_interval_milliseconds = 200\nrun_native_checks = false\n");
        var log = DispatchProxy.Create(api.GetType("Lustral.ILog", true)!, typeof(OfflineLog));
        var configType = core.GetType("Lustral.Core.Config.TomlModConfig", true)!;
        var config = Activator.CreateInstance(configType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [file, "Quantum offline config migration", log], null)!;
        var bind = configType.GetMethod("Bind")!.MakeGenericMethod(settingsType);
        var migrated = bind.Invoke(config, null)!;
        Check(!(bool)settingsType.GetProperty("Enabled")!.GetValue(migrated)! &&
            settingsType.GetProperty("LogLevel")!.GetValue(migrated)!.ToString()=="Warn" &&
            (int)settingsType.GetProperty("PollIntervalMilliseconds")!.GetValue(migrated)! == 200,
            "actual loader config migration preserves old user preferences");
        Check(File.ReadAllText(file).Contains("dust_distribution = \"RecipeBalanced\"") &&
            File.ReadAllText(file).Contains("packager_dust_reserve_kilograms = 0") &&
            File.ReadAllText(file).Contains("enable_coop = true") && File.ReadAllText(file).Contains("max_transfers_per_tick = 128") &&
            File.ReadAllText(file).Contains("collect_garage_dust = true") && File.ReadAllText(file).Contains("enable_dashboard = true") &&
            File.ReadAllText(file).Contains("reduce_dashboard_motion = false"),
            "actual loader adds missing routing settings to old config");
        File.WriteAllText(file, "dust_distribution = \"CapacityFair\"\nchamber_dust_priority = -3\npress_dust_priority = 8\npress_dust_buffer_kilograms = 0.5\npackager_dust_reserve_kilograms = 3.5\n");
        var customized = bind.Invoke(config, null)!;
        Check(settingsType.GetProperty("DustDistribution")!.GetValue(customized)!.ToString()=="CapacityFair" &&
            (int)settingsType.GetProperty("PressDustPriority")!.GetValue(customized)! == 8 &&
            (int)settingsType.GetProperty("ChamberDustPriority")!.GetValue(customized)! == -3 &&
            (double)settingsType.GetProperty("PressDustBufferKilograms")!.GetValue(customized)! == 0.5 &&
            (double)settingsType.GetProperty("PackagerDustReserveKilograms")!.GetValue(customized)! == 3.5,
            "actual loader parses routing enum, priorities and fractional reserves");
        Check(((OfflineLog)log).Messages.Count==0, "config migration and customization produce no loader warnings/errors");
        File.Copy(Path.GetFullPath("distribution/default-config.toml"), file, true);
        var shipped = bind.Invoke(config, null)!;
        var defaults = Activator.CreateInstance(settingsType)!;
        Check(settingsType.GetProperties().All(p => Equals(p.GetValue(shipped), p.GetValue(defaults))),
            "shipped default config matches all runtime setting defaults through actual loader");
        settingsType.GetProperty("Enabled")!.SetValue(shipped,false);
        configType.GetMethod("Save")!.MakeGenericMethod(settingsType).Invoke(config,[shipped]);
        var disabled=bind.Invoke(config,null)!;
        Check(!(bool)settingsType.GetProperty("Enabled")!.GetValue(disabled)!,"actual loader saves and reloads disabled transport setting");
        settingsType.GetProperty("Enabled")!.SetValue(disabled,true);
        configType.GetMethod("Save")!.MakeGenericMethod(settingsType).Invoke(config,[disabled]);
        Check((bool)settingsType.GetProperty("Enabled")!.GetValue(bind.Invoke(config,null)!)!,"actual loader saves and reloads re-enabled transport setting");
    }
    catch (Exception error) { Check(false, "offline loader config migration: " + error.GetBaseException().Message); }
    finally
    {
        // This unique temporary directory contains only the test config/schema we just created.
        foreach (string file in Directory.GetFiles(temp)) File.Delete(file);
        Directory.Delete(temp);
    }
    Console.WriteLine(JsonSerializer.Serialize(new { success=failures==0, failures, checks=results, scope="Offline reflection and file inspection only; no Unity game process or native gameplay methods executed." },new JsonSerializerOptions{WriteIndented=true}));
    Environment.ExitCode=failures==0?0:1;
    return;
}

string[] wanted = args.Length > 0 ? args : ["MoonWorld", "MoonProductionState", "MoonCrew", "MoonDepotSnapshot", "MoonVehicleStorageSnapshot", "MoonCargo"];
foreach (var type in assembly.GetTypes().Where(t => wanted.Contains(t.Name) || wanted.Contains(t.FullName!)))
{
    Console.WriteLine("TYPE " + type.FullName);
    foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        if (!field.Name.StartsWith("Native")) Console.WriteLine("FIELD " + field);
    foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        Console.WriteLine("PROPERTY " + property);
    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        if (!method.IsSpecialName) Console.WriteLine("METHOD " + (method.IsStatic ? "static " : "instance ") + method);
    foreach (var ctor in type.GetConstructors()) Console.WriteLine("CTOR " + ctor);
}

public class OfflineLog : DispatchProxy
{
    public List<string> Messages { get; } = [];
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name is "Warn" or "Error") Messages.Add(args?[0]?.ToString() ?? "");
        return null;
    }
}

internal static class IlCalls
{
    private static readonly Dictionary<short,System.Reflection.Emit.OpCode> Codes=typeof(System.Reflection.Emit.OpCodes)
        .GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.FieldType==typeof(System.Reflection.Emit.OpCode))
        .Select(f=>(System.Reflection.Emit.OpCode)f.GetValue(null)!).ToDictionary(c=>c.Value);
    internal static IEnumerable<MethodBase> Read(MethodBase method)
    {
        byte[]? il=method.GetMethodBody()?.GetILAsByteArray();
        if(il==null) yield break;
        for(int p=0;p<il.Length;)
        {
            short value=il[p++];
            if(value==0xfe) value=unchecked((short)(0xfe00|il[p++]));
            var code=Codes[value];
            if(code.OperandType==System.Reflection.Emit.OperandType.InlineMethod)
                yield return method.Module.ResolveMethod(BitConverter.ToInt32(il,p),method.DeclaringType?.GetGenericArguments(),method.IsGenericMethod?method.GetGenericArguments():null)!;
            p+=code.OperandType switch
            {
                System.Reflection.Emit.OperandType.InlineNone=>0,
                System.Reflection.Emit.OperandType.ShortInlineBrTarget or System.Reflection.Emit.OperandType.ShortInlineI or System.Reflection.Emit.OperandType.ShortInlineVar=>1,
                System.Reflection.Emit.OperandType.InlineVar=>2,
                System.Reflection.Emit.OperandType.InlineI8 or System.Reflection.Emit.OperandType.InlineR=>8,
                System.Reflection.Emit.OperandType.InlineSwitch=>4+4*BitConverter.ToInt32(il,p),
                _=>4
            };
        }
    }
}
