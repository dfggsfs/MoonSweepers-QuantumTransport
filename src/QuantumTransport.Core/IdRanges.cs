namespace QuantumTransport.Core;

public readonly record struct IdRange(int First, int Last);
public readonly record struct RangeChange(int[] RemovedHeads, IdRange Range);

/// <summary>Exact consumed-object receipts compressed into disjoint ranges; no TTL that could admit a replay.</summary>
public sealed class IdRanges
{
    private readonly List<IdRange> _ranges = [];
    public IReadOnlyList<IdRange> Ranges => _ranges;
    private int LowerBound(int id)
    {
        int lo = 0, hi = _ranges.Count;
        while (lo < hi) { int mid = lo + (hi - lo) / 2; if (_ranges[mid].First < id) lo = mid + 1; else hi = mid; }
        return lo;
    }
    public bool Contains(int id)
    {
        int i = LowerBound(id);
        return i < _ranges.Count && _ranges[i].First == id || i > 0 && _ranges[i - 1].Last >= id;
    }
    public RangeChange? Add(int id) => AddRange(id, id);
    public int NextAvailable(int current, int block, int size)
    {
        long end=(long)block+size;
        if(block<0 || size<=0 || current<block || current>=end) throw new ArgumentOutOfRangeException(nameof(current));
        long next=current;
        foreach(var range in _ranges)
            if(range.Last>=block && range.First<end) next=Math.Max(next,Math.Min(end,(long)range.Last+1));
        if(next>=end || next>int.MaxValue) throw new InvalidOperationException("Native cargo ID block exhausted");
        return (int)next;
    }
    public RangeChange? AddRange(int first, int last)
    {
        if (first <= 0 || last < first) throw new ArgumentOutOfRangeException(nameof(first));
        if (first == last && Contains(first)) return null;
        int i = LowerBound(first);
        if (i > 0 && (long)_ranges[i - 1].Last + 1 >= first) i--;
        var removed = new List<int>();
        while (i < _ranges.Count && (long)_ranges[i].First <= (long)last + 1)
        {
            first = Math.Min(first, _ranges[i].First); last = Math.Max(last, _ranges[i].Last);
            removed.Add(_ranges[i].First); _ranges.RemoveAt(i);
        }
        var range = new IdRange(first, last);
        _ranges.Insert(i, range);
        return new(removed.ToArray(), range);
    }
}
