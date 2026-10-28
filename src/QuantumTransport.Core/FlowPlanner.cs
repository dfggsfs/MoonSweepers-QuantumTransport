namespace QuantumTransport.Core;

public readonly record struct ReceiverSpace(int Id, double FreeKilograms);
public readonly record struct TransferAllocation(int ReceiverId, double Kilograms);

/// <summary>Divides a finite supply fairly across compatible receivers, respecting each capacity.</summary>
public static class FlowPlanner
{
    public const double Epsilon = 1e-8;

    public static IReadOnlyList<TransferAllocation> Allocate(double supply, IReadOnlyList<ReceiverSpace> receivers)
    {
        if (!double.IsFinite(supply) || supply < 0)
            throw new ArgumentOutOfRangeException(nameof(supply));

        var ids = new HashSet<int>();
        var active = new List<(int Id, double Free)>();
        foreach (var receiver in receivers)
        {
            if (!ids.Add(receiver.Id))
                throw new ArgumentException("Receiver IDs must be unique", nameof(receivers));
            if (!double.IsFinite(receiver.FreeKilograms) || receiver.FreeKilograms < 0)
                throw new ArgumentOutOfRangeException(nameof(receivers));
            if (receiver.FreeKilograms > Epsilon)
                active.Add((receiver.Id, receiver.FreeKilograms));
        }

        var sorted = active.OrderBy(r => r.Free).ToArray();
        double remaining = supply;
        double level = double.PositiveInfinity;
        for (int i = 0; i < sorted.Length; i++)
        {
            double share = Math.Max(0, remaining) / (sorted.Length - i);
            if (share < sorted[i].Free) { level = share; break; }
            remaining -= sorted[i].Free;
        }
        remaining = supply;
        var results = new List<TransferAllocation>(active.Count);
        foreach (var receiver in active)
        {
            double amount = Math.Min(remaining, Math.Min(level, receiver.Free));
            if (amount <= Epsilon) continue;
            results.Add(new(receiver.Id, amount)); remaining -= amount;
        }
        return results;
    }
}
