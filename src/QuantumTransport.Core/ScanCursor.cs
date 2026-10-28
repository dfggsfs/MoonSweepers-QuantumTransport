namespace QuantumTransport.Core;

public static class ScanCursor
{
    // Advance by work actually visited, including when a time budget cuts a window short.
    // A whole-catalog scan still rotates its starting source to share a truncated transfer plan.
    public static int Advance(int cursor,int examined,int count) => count<=0?0:examined<=0?cursor%count:
        (cursor+(examined>=count?1:examined))%count;
}
