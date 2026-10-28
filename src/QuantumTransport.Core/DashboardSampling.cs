namespace QuantumTransport.Core;

public static class DashboardSampling
{
    // Interleave source categories so a large factory cannot hide the smaller
    // player/vehicle/garage groups in a bounded read-only dashboard sample.
    public static IReadOnlyList<int> Select(IReadOnlyList<string> groups, int limit)
    {
        if(limit<0) throw new ArgumentOutOfRangeException(nameof(limit));
        var queues=new Dictionary<string,Queue<int>>(StringComparer.Ordinal);
        for(int i=0;i<groups.Count;i++)
        {
            if(!queues.TryGetValue(groups[i],out var queue)) queues[groups[i]]=queue=new();
            queue.Enqueue(i);
        }
        var result=new List<int>(Math.Min(limit,groups.Count));
        while(result.Count<limit)
        {
            bool progress=false;
            foreach(var queue in queues.Values)
            {
                if(result.Count>=limit) break;
                if(queue.Count==0) continue;
                result.Add(queue.Dequeue()); progress=true;
            }
            if(!progress) break;
        }
        return result;
    }
}
