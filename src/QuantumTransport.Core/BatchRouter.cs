namespace QuantumTransport.Core;

public readonly record struct SourceOffer(int Index, int MachineId, double Kilograms);
public readonly record struct BatchMove(int SourceIndex, int ReceiverId, double Kilograms);

public static class BatchRouter
{
    // Each current recipe routes between distinct machine classes. The self check also
    // protects future resource adapters; unmatched quantities always stay at the source.
    public static IReadOnlyList<BatchMove> Plan(ResourceKind resource, IReadOnlyList<SourceOffer> sources,
        IReadOnlyList<ReceiverPort> receivers, int maxMoves)
    {
        if (maxMoves <= 0) return [];
        var ids = new HashSet<int>();
        double supply = 0;
        foreach (var source in sources)
        {
            if (!ids.Add(source.Index) || !double.IsFinite(source.Kilograms) || source.Kilograms < 0)
                throw new ArgumentException("Invalid or repeated source offer");
            supply += source.Kilograms;
        }
        if (!double.IsFinite(supply)) throw new ArgumentOutOfRangeException(nameof(sources));
        var allocations = ResourceRouter.Allocate(resource, int.MinValue, supply, receivers);
        var remaining = sources.Select(s => s.Kilograms).ToArray();
        var result = new List<BatchMove>();
        int cursor = 0;
        foreach (var target in allocations)
        {
            double need = target.Kilograms;
            int examined = 0;
            while (need > FlowPlanner.Epsilon && examined < sources.Count && result.Count < maxMoves)
            {
                int i = cursor % sources.Count;
                if (remaining[i] <= FlowPlanner.Epsilon || sources[i].MachineId == target.ReceiverId)
                { cursor = (cursor + 1) % sources.Count; examined++; continue; }
                double amount = Math.Min(need, remaining[i]);
                result.Add(new(sources[i].Index, target.ReceiverId, amount));
                remaining[i] -= amount; need -= amount;
                if (remaining[i] <= FlowPlanner.Epsilon) { cursor = (cursor + 1) % sources.Count; examined++; }
            }
            if (result.Count >= maxMoves) break;
        }
        return result;
    }
}
