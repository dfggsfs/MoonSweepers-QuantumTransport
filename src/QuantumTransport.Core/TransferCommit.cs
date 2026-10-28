namespace QuantumTransport.Core;

public readonly record struct CommittedTransfer(double Kilograms, double SourceBefore, double SourceAfter, double TargetBefore, double TargetAfter);

/// <summary>Commits one main-thread inventory move, restoring both balances if either native operation fails.</summary>
public static class TransferCommit
{
    public static CommittedTransfer Apply(double amount, Func<double> readSource, Action<double> writeSource,
        Func<double> readTarget, Action<double> addTarget, Action restoreTarget)
    {
        if (!double.IsFinite(amount) || amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        double source = readSource();
        double target = readTarget();
        if (!double.IsFinite(source) || !double.IsFinite(target) || source < amount || target < 0)
            throw new InvalidOperationException("Inventory balance is invalid or source is insufficient");
        try
        {
            addTarget(amount);
            writeSource(source - amount);
            double afterSource = readSource();
            double afterTarget = readTarget();
            if (!double.IsFinite(afterSource) || !double.IsFinite(afterTarget) ||
                Math.Abs((source-afterSource)-amount) > 1e-7 || Math.Abs((afterTarget-target)-amount) > 1e-7)
                throw new InvalidOperationException("Native inventory conservation check failed");
            return new(amount, source, afterSource, target, afterTarget);
        }
        catch (Exception original)
        {
            var errors = new List<Exception> { original };
            try { writeSource(source); } catch (Exception error) { errors.Add(error); }
            try { restoreTarget(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count > 1) throw new AggregateException("Inventory commit failed and rollback was incomplete; transport must stop", errors);
            throw;
        }
    }
}
