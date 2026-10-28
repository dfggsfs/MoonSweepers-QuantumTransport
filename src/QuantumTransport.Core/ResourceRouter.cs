namespace QuantumTransport.Core;

public enum ResourceKind { Dust, Sand, Stone }
public readonly record struct ReceiverPort(int MachineId, ResourceKind Resource, double FreeKilograms, int Priority = 0);

public static class ResourceRouter
{
    public static IReadOnlyList<TransferAllocation> Allocate(ResourceKind resource, int sourceMachineId, double supply,
        IReadOnlyList<ReceiverPort> ports)
    {
        if (!double.IsFinite(supply) || supply < 0) throw new ArgumentOutOfRangeException(nameof(supply));
        var compatible = ports.Where(p => p.Resource == resource && p.MachineId != sourceMachineId).ToArray();
        // Validate across tiers too: a receiver must never receive twice through duplicate ports.
        var ids = new HashSet<int>();
        foreach (var port in compatible)
        {
            if (!ids.Add(port.MachineId)) throw new ArgumentException("Receiver IDs must be unique per resource", nameof(ports));
            if (!double.IsFinite(port.FreeKilograms) || port.FreeKilograms < 0) throw new ArgumentOutOfRangeException(nameof(ports));
        }
        var result = new List<TransferAllocation>();
        double remaining = supply;
        foreach (var tier in compatible.GroupBy(p => p.Priority).OrderByDescending(g => g.Key))
        {
            if (remaining <= FlowPlanner.Epsilon) break;
            var allocated = FlowPlanner.Allocate(remaining, tier.Select(p => new ReceiverSpace(p.MachineId, p.FreeKilograms)).ToArray());
            result.AddRange(allocated);
            remaining = Math.Max(0, remaining - allocated.Sum(a => a.Kilograms));
        }
        return result;
    }
}
