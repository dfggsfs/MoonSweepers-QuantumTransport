namespace QuantumTransport.Core;

public readonly record struct CommittedDelivery(double Kilograms, double SourceBefore, double SourceAfter,
    double StationBefore, double StationAfter);

/// <summary>Debit only the amount actually accepted by the native station; restore accounting on failure.</summary>
public static class DeliveryCommit
{
    public static CommittedDelivery Apply(Func<double> readSource, Action<double> writeSource,
        Func<double> readStation, Func<double, double> deposit, Action restoreStation)
    {
        double source = readSource(), station = readStation();
        if (!double.IsFinite(source) || source < 0 || !double.IsFinite(station) || station < 0)
            throw new InvalidOperationException("Invalid delivery inventory");
        if (source <= FlowPlanner.Epsilon) return new(0, source, source, station, station);
        try
        {
            double accepted = deposit(source);
            if (!double.IsFinite(accepted) || accepted < 0 || accepted > source)
                throw new InvalidOperationException("Native station returned an invalid accepted amount");
            // A nearly full station can expose a sub-epsilon rounding gap. Undo that
            // credit instead of repeatedly crediting an amount too small to debit reliably.
            if (accepted > 0 && accepted <= FlowPlanner.Epsilon)
            {
                restoreStation();
                double restoredStation = readStation(), restoredSource = readSource();
                if (!double.IsFinite(restoredStation) || !double.IsFinite(restoredSource) ||
                    Math.Abs(restoredStation - station) > 1e-7 || Math.Abs(restoredSource - source) > 1e-7)
                    throw new InvalidOperationException("Tiny delivery rollback did not restore inventory");
                return new(0, source, source, station, station);
            }
            if (accepted > 0) writeSource(source - accepted);
            double afterSource = readSource(), afterStation = readStation();
            if (!double.IsFinite(afterSource) || !double.IsFinite(afterStation) ||
                Math.Abs((source - afterSource) - accepted) > 1e-7 ||
                Math.Abs((afterStation - station) - accepted) > 1e-7)
                throw new InvalidOperationException("Native station delivery conservation check failed");
            return new(accepted, source, afterSource, station, afterStation);
        }
        catch (Exception original)
        {
            var errors = new List<Exception> { original };
            try { writeSource(source); } catch (Exception error) { errors.Add(error); }
            try { restoreStation(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count > 1) throw new AggregateException("Delivery failed and rollback was incomplete; transport must stop", errors);
            throw;
        }
    }
}
