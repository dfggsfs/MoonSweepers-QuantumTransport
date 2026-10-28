using System.Globalization;
using Il2CppMoonCleaners;
using QuantumTransport.Core;
using GameString = Il2CppSystem.String;

namespace QuantumTransport;

internal sealed class CapsuleSources(string prefix = "qt.receipt.v1:")
{
    private string Prefix => prefix;
    private IdRanges _receipts = new();
    private bool _loaded;
    internal int ReceiptRanges => _receipts.Ranges.Count;
    internal void Reset() { _loaded = false; _receipts = new(); }
    internal bool Contains(MoonWorld world,int id) { Load(world); return _receipts.Contains(id); }
    internal void Record(MoonWorld world,int id) { Load(world); Mark(world,id); }
    internal int NextAvailable(MoonWorld world,int current,int block,int size) { Load(world); return _receipts.NextAvailable(current,block,size); }

    private void Load(MoonWorld world)
    {
        if (_loaded) return;
        var entries = world.vehicleStorage.GetEnumerator();
        while ((bool)entries.MoveNext())
        {
            string key = entries.Current.Key;
            var value = entries.Current.Value;
            if (value == null || !key.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            if (!int.TryParse(key[Prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int first) ||
                first <= 0 || (double)value.kilograms != 0 || !double.IsFinite(value.capacity) ||
                (double)value.capacity < first || (double)value.capacity > int.MaxValue || (double)value.capacity != Math.Truncate(value.capacity))
                throw new InvalidOperationException("Invalid persisted cargo receipt range");
            _receipts.AddRange(first, (int)(double)value.capacity);
        }
        _loaded = true;
    }

    private void Mark(MoonWorld world, int id)
    {
        var change = _receipts.Add(id);
        if (change == null) return;
        var range = change.Value.Range;
        foreach (int head in change.Value.RemovedHeads)
            if (head != range.First) world.vehicleStorage.Remove((GameString)(Prefix + head.ToString(CultureInfo.InvariantCulture)));
        GameString key = Prefix + range.First.ToString(CultureInfo.InvariantCulture);
        if ((bool)world.vehicleStorage.ContainsKey(key)) world.vehicleStorage.get_Item(key).capacity = range.Last;
        else world.vehicleStorage.set_Item(key, new MoonVehicleStorageSnapshot { id=key, kilograms=0, capacity=range.Last });
        world.progressDirty = true;
    }

    internal void Add(MoonWorld world, List<ResourceSource> sources)
    {
        Load(world);
        var entries = world.canisters.GetEnumerator();
        while ((bool)entries.MoveNext())
        {
            var c = entries.Current.Value;
            int id = c.id;
            if (_receipts.Contains(id))
            {
                // Defer removal until outside this native dictionary enumeration.
                sources.Add(new(-1, ResourceKind.Dust, "duplicate_capsule", c.owner, () => 0, _ => { },
                    Exists: () => (bool)world.canisters.ContainsKey(id),
                    AfterCommit: () => { world.RemoveCanister(id); world.RaiseMachineCanister(MoonWorld.CanisterOp.Removed, c); }));
                continue;
            }
            ResourceKind? resource = c.cargo.Equals(MoonCargo.Dust) ? ResourceKind.Dust :
                c.cargo.Equals(MoonCargo.Sand) ? ResourceKind.Sand :
                c.cargo.Equals(MoonCargo.Stone) ? ResourceKind.Stone : null;
            if (resource == null || !SourceRegistry.ValidOwner(c.owner)) continue;
            var binding = new Binding(this, world, c, resource.Value);
            sources.Add(binding.Source);
        }
    }

    private sealed class Binding
    {
        private readonly CapsuleSources _owner;
        private readonly MoonWorld _world;
        private MoonDroppedCanister _template;
        private readonly int _id;
        internal ResourceSource Source { get; }
        internal Binding(CapsuleSources owner, MoonWorld world, MoonDroppedCanister c, ResourceKind kind)
        {
            _owner=owner; _world=world; _template=c; _id=c.id;
            string contributor=c.owner;
            Source = new(-1, kind, "network_capsule", contributor, Read, Write, Exists: () =>
                !_owner._receipts.Contains(_id) && (bool)world.canisters.ContainsKey(_id) &&
                (string)world.canisters.get_Item(_id).owner == contributor && world.canisters.get_Item(_id).cargo.Equals(c.cargo) &&
                !(bool)world.IsCanisterCarried(_id) && !(bool)world.IsCanisterFlying(_id) && !(bool)world.IsCanisterRiding(_id),
                AfterCommit: Notify);
        }
        private double Read()
        {
            if (!(bool)_world.canisters.ContainsKey(_id)) return 0;
            _template = _world.canisters.get_Item(_id);
            return _template.kilograms;
        }
        private void Write(double value)
        {
            if (value <= FlowPlanner.Epsilon) { _world.RemoveCanister(_id); return; }
            var c = _template; c.kilograms=value;
            if (!(bool)_world.ApplyCrewCanister(MoonWorld.CanisterOp.Upsert, c, null))
                throw new InvalidOperationException("Native cargo inventory update rejected");
        }
        private void Notify()
        {
            if ((bool)_world.canisters.ContainsKey(_id))
                _world.RaiseMachineCanister(MoonWorld.CanisterOp.Upsert, _world.canisters.get_Item(_id));
            else { _owner.Mark(_world, _id); _world.RaiseMachineCanister(MoonWorld.CanisterOp.Removed, _template); }
        }
    }
}
