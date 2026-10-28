namespace QuantumTransport.Core;

/// <summary>Convert one client-owned inventory into one native, uniquely numbered cargo object before publishing it.</summary>
public static class HandoffCommit
{
    public static double Apply(double requested, bool sourceIsBag, Func<double> readSource, Action<double> writeSource,
        Func<double> readBag, Action<double> writeBag, Func<double, double> pack, Action discardUnpublishedCargo)
    {
        double source = readSource(), bag = readBag();
        if (!double.IsFinite(requested) || requested <= 0 || !double.IsFinite(source) || source < requested ||
            !double.IsFinite(bag) || bag < 0 || !double.IsFinite(bag + requested) || sourceIsBag && Math.Abs(source-bag)>1e-7)
            throw new InvalidOperationException("Invalid client handoff balances");
        try
        {
            if (!sourceIsBag) { writeSource(source - requested); writeBag(bag + requested); }
            double packed = pack(requested);
            if (!double.IsFinite(packed) || packed < 0 || packed > requested)
                throw new InvalidOperationException("Invalid native packed amount");
            double expectedBag = (sourceIsBag ? bag : bag + requested) - packed;
            if (!double.IsFinite(readBag()) || Math.Abs(readBag() - expectedBag) > 1e-7)
                throw new InvalidOperationException("Native packing debit does not match cargo");
            if (!sourceIsBag) writeBag(bag);
            writeSource(source - packed);
            if (!double.IsFinite(readSource()) || !double.IsFinite(readBag()) || Math.Abs(readSource() - (source - packed)) > 1e-7 ||
                Math.Abs(readBag() - (sourceIsBag ? bag - packed : bag)) > 1e-7)
                throw new InvalidOperationException("Client handoff conservation failed");
            return packed;
        }
        catch (Exception original)
        {
            var errors = new List<Exception> { original };
            try { discardUnpublishedCargo(); } catch (Exception error) { errors.Add(error); }
            try { writeSource(source); } catch (Exception error) { errors.Add(error); }
            if (!sourceIsBag) try { writeBag(bag); } catch (Exception error) { errors.Add(error); }
            if (errors.Count > 1) throw new AggregateException("Client handoff rollback incomplete; stop transport", errors);
            throw;
        }
    }
}
