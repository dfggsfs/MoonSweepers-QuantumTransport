using Il2CppMoonCleaners;
using QuantumTransport.Core;
using GameString = Il2CppSystem.String;

namespace QuantumTransport;

internal sealed class CollectionSources
{
    private MoonWorld? _viewWorld;
    private MoonPrototypeVehicle[] _vehicleViews = [];
    private long _lastVehicleScan;
    private SourceRegistry _registry=new();
    private Dictionary<string,Func<string?>> _controllers=new(StringComparer.Ordinal);

    internal bool ControlsVehicle(MoonWorld world,string player,string storage,string owner)
    {
        var owned=world.OwnedVehicleAt(storage);
        if(owned!=null) return (string)owned.owner==owner && (string)owned.holder==player;
        if(!_registry.TryResolve(storage,out var identity) || identity.Kind!="vehicle" || identity.Owner!=owner) return false;
        string? controller=_controllers.TryGetValue(storage,out var read)?read():null;
        return controller==player;
    }

    internal void Reset()
    {
        _viewWorld = null;
        _vehicleViews = [];
        _lastVehicleScan = 0;
        _registry=new(); _controllers.Clear();
    }

    internal int Add(MoonWorld world, Settings settings, List<ResourceSource> sources,
        TransportRole role = TransportRole.SinglePlayer, string localPlayer = "")
    {
        if (settings.CollectPlayerDust) AddPlayers(world, sources, role, localPlayer);
        if (!settings.CollectVehicleDust && !settings.CollectRobotDust && !settings.CollectGarageDust) return 0;
        var registry = _registry=new SourceRegistry();
        var controllers = _controllers=new Dictionary<string,Func<string?>>(StringComparer.Ordinal);
        var nearDocks = new HashSet<int>();
        var garages = new PrefixIndex<int>();
        if (settings.CollectGarageDust && role != TransportRole.Client)
        {
            var depots = world.depots.GetEnumerator();
            while ((bool)depots.MoveNext())
            {
                var depot = depots.Current.Value;
                if (depot != null && (bool)world.IsGarage(depot.id))
                    garages.Add(MoonWorld.GarageSiloPrefix(depot.id), depot.id);
            }
        }
        // Build the identity map for both categories so disabled sources are not reported as unknown.
        {
            if (world.ownedVehicles != null)
            {
                var vehicles = world.ownedVehicles.GetEnumerator();
                while ((bool)vehicles.MoveNext())
                {
                    var vehicle = vehicles.Current.Value;
                    if (vehicle != null) registry.Register(vehicle.Storage, new(vehicle.owner, "vehicle", Controller: vehicle.holder));
                }
            }
            // Starter and currently driven prototype vehicles also have explicit owner/storage registrations.
            if (world.garageVehicles != null)
            {
                var vehicles = world.garageVehicles.GetEnumerator();
                while ((bool)vehicles.MoveNext())
                {
                    var vehicle = vehicles.Current.Value;
                    RegisterVehicleView(registry, world, vehicle,controllers);
                }
            }
            // Starter prototype vehicles are not necessarily in the purchased/garage dictionaries.
            // Discover their real components once a second, then read fresh owner data each poll.
            long now = Environment.TickCount64;
            if (_viewWorld != world || now - _lastVehicleScan >= 1000)
            {
                _viewWorld = world;
                _lastVehicleScan = now;
                var found = UnityEngine.Object.FindObjectsByType<MoonPrototypeVehicle>(UnityEngine.FindObjectsSortMode.None);
                _vehicleViews = Enumerable.Range(0, (int)found.Length).Select(i => found[i]).ToArray();
            }
            foreach (var vehicle in _vehicleViews)
                RegisterVehicleView(registry, world, vehicle,controllers);
        }
        {
            var depots = world.depots.GetEnumerator();
            while ((bool)depots.MoveNext())
            {
                var depot = depots.Current.Value;
                if (depot == null || !(bool)MoonWorld.IsRobotDock(depot.kind)) continue;
                RegisterRobot(registry, depot.id, depot.owner);
            }
            var docks = MoonRobotDock.All;
            if (docks != null)
                for (int i = 0; i < (int)docks.Count; i++)
                {
                    var dock = docks[i];
                    if (dock is null || dock == null || dock!.World != world) continue;
                    nearDocks.Add(dock.DockId);
                    RegisterRobot(registry, dock.DockId, dock.Owner);
                    registry.Register(dock.StorageId, new(dock.Owner, "robot", dock.DockId));
                }
        }

        if (settings.CollectRobotDust && role != TransportRole.Client && world.farRobots != null)
        {
            var robots = world.farRobots.GetEnumerator();
            while ((bool)robots.MoveNext())
            {
                var pair = robots.Current;
                int id = pair.Key;
                var robot = pair.Value;
                // Far rovers keep confirmed post-CommitRegion cargo here. work/need are
                // unfinished progress and must never be transported. Near-view handover
                // switches to its canonical native storage, so never read both forms.
                if (robot == null || nearDocks.Contains(id) ||
                    !(bool)world.depots.ContainsKey(id)) continue;
                var depot = world.depots.get_Item(id);
                if (depot == null || !(bool)MoonWorld.IsRobotDock(depot.kind) || !SourceRegistry.ValidOwner(depot.owner)) continue;
                sources.Add(new(id, ResourceKind.Dust, "robot_far", depot.owner,
                    () => (bool)world.farRobots.ContainsKey(id) ? (double)world.farRobots.get_Item(id).kilograms : 0,
                    value => world.farRobots.get_Item(id).kilograms = value,
                    Exists: () => (bool)world.farRobots.ContainsKey(id) && (bool)world.depots.ContainsKey(id)));
            }
        }

        int unresolved = 0;
        var storages = world.vehicleStorage.GetEnumerator();
        while ((bool)storages.MoveNext())
        {
            var pair = storages.Current;
            var storage = pair.Value;
            if (storage == null) continue;
            string id = pair.Key;
            // Vanilla UnloadIntoGarage stores each contributor separately under
            // GarageSiloPrefix(garage) + player. Do not assign depot.owner here.
            if (settings.CollectGarageDust && role != TransportRole.Client &&
                garages.TryMatch(id, out int garage, out int prefixLength))
            {
                if (!MachineStorageIdentity.TryResolve(garages, id, storage.id, out garage, out string contributor))
                { unresolved++; continue; }
                GameString garageKey = pair.Key;
                sources.Add(new(garage, ResourceKind.Dust, "garage", contributor,
                    () => (bool)world.vehicleStorage.ContainsKey(garageKey) ? (double)world.vehicleStorage.get_Item(garageKey).kilograms : 0,
                    value => world.vehicleStorage.get_Item(garageKey).kilograms = value,
                    Exists: () => (bool)world.vehicleStorage.ContainsKey(garageKey) && (bool)world.depots.ContainsKey(garage) &&
                        (bool)world.IsGarage(garage), StorageId: id));
                continue;
            }
            if (!registry.TryResolve(id, out var identity))
            {
                // Packagers and other machine buffers are handled separately; never infer an owner from a prefix.
                if ((double)storage.kilograms > FlowPlanner.Epsilon && !(bool)MoonWorld.MachineStorage(pair.Key)) unresolved++;
                continue;
            }
            if ((identity.Kind == "vehicle" && !settings.CollectVehicleDust) ||
                (identity.Kind == "robot" && (!settings.CollectRobotDust || role == TransportRole.Client))) continue;
            if (!string.Equals(id, (string)storage.id, StringComparison.Ordinal)) { unresolved++; continue; }
            GameString key = pair.Key;
            string? Controller()
            {
                if (identity.Kind != "vehicle") return null;
                if (world.ownedVehicles != null && (bool)world.ownedVehicles.ContainsKey(key))
                    return world.ownedVehicles.get_Item(key).holder;
                return (controllers.TryGetValue(id,out var read)?read():null)??"__qt_unknown_controller__";
            }
            sources.Add(new(identity.MachineId, ResourceKind.Dust, identity.Kind, identity.Owner,
                () => (bool)world.vehicleStorage.ContainsKey(key) ? (double)world.vehicleStorage.get_Item(key).kilograms : 0,
                value => world.vehicleStorage.get_Item(key).kilograms = value,
                Exists: () => (bool)world.vehicleStorage.ContainsKey(key) &&
                    CoopPolicy.MayRead(role, identity.Kind, identity.Owner, Controller(), localPlayer),StorageId:id));
        }
        return unresolved;
    }

    private static void RegisterRobot(SourceRegistry registry, int id, string owner)
    {
        var identity = new SourceIdentity(owner, "robot", id);
        registry.Register(MoonWorld.RobotStorageId(id), identity);
        registry.Register(MoonWorld.DockStorageId(id), identity);
    }

    private static void RegisterVehicleView(SourceRegistry registry, MoonWorld world, MoonPrototypeVehicle? vehicle,Dictionary<string,Func<string?>> controllers)
    {
        if (vehicle == null) return;
        // FindObjectsByType may include a component that has not completed Initialize.
        // Read backing owner/player fields before calling owner-dependent properties.
        var equipment = vehicle!.owner;
        if (equipment == null) return;
        var driver = equipment!.Player;
        if (driver == null || driver!.world != world) return;
        string storage=vehicle.StorageId;
        controllers[storage]=()=>
        {
            if(vehicle==null) return null;
            var current=vehicle!.owner;
            if(current==null) return null;
            var player=current!.Player;
            return player!=null && player!.world==world && (string)vehicle.StorageId==storage?(string)player.UpgradePlayer:null;
        };
        // Purchased vehicle ownership takes precedence over its current driver.
        if (!registry.TryResolve(vehicle.StorageId, out _))
            registry.Register(vehicle.StorageId, new(driver!.UpgradePlayer, "vehicle", Controller: driver.UpgradePlayer));
    }

    private static void AddPlayers(MoonWorld world, List<ResourceSource> sources, TransportRole role, string localPlayer)
    {
        if (world.inventoryMass != null)
        {
            var inventories = world.inventoryMass.GetEnumerator();
            while ((bool)inventories.MoveNext())
            {
                var pair = inventories.Current;
                GameString key = pair.Key;
                string owner = key;
                if (!SourceRegistry.ValidOwner(owner) || !CoopPolicy.MayRead(role, "player", owner, owner, localPlayer)) continue;
                sources.Add(new(-1, ResourceKind.Dust, "player", owner,
                    () => (bool)world.inventoryMass.ContainsKey(key) ? (double)world.inventoryMass.get_Item(key) : 0,
                    value => world.inventoryMass.set_Item(key, value),
                    Exists: () => (bool)world.inventoryMass.ContainsKey(key)));
            }
        }
        if (world.toolMass == null) return;
        var players = world.toolMass.GetEnumerator();
        while ((bool)players.MoveNext())
        {
            var player = players.Current;
            string owner = player.Key;
            var containers = player.Value;
            if (!SourceRegistry.ValidOwner(owner) || containers == null || !CoopPolicy.MayRead(role, "player_container", owner, owner, localPlayer)) continue;
            string loaded = world.LoadedContainerId(player.Key);
            var entries = containers.GetEnumerator();
            while ((bool)entries.MoveNext())
            {
                var entry = entries.Current;
                GameString key = entry.Key;
                // The loaded container is represented by inventoryMass, never read it twice.
                if (!SourceRegistry.IsStoredContainerSource(key, loaded)) continue;
                bool Exists()=>(bool)world.toolMass.ContainsKey(player.Key) && world.toolMass.get_Item(player.Key)!=null &&
                    (bool)world.toolMass.get_Item(player.Key).ContainsKey(key) && SourceRegistry.IsStoredContainerSource(key,world.LoadedContainerId(player.Key));
                sources.Add(new(-1, ResourceKind.Dust, "player_container", owner,
                    () => Exists()?(double)world.toolMass.get_Item(player.Key).get_Item(key):0,
                    value => world.toolMass.get_Item(player.Key).set_Item(key,value),Exists:Exists));
            }
        }
    }
}
