namespace QuantumTransport.Core;

public static class MachineStorageIdentity
{
    public static bool TryResolve(PrefixIndex<int> prefixes, string key, string snapshotId,
        out int machine, out string contributor)
    {
        machine = -1; contributor = "";
        if (!string.Equals(key, snapshotId, StringComparison.Ordinal) ||
            !prefixes.TryMatch(key, out int id, out int length)) return false;
        string owner = key[length..];
        if (!SourceRegistry.ValidOwner(owner)) return false;
        machine = id; contributor = owner;
        return true;
    }
}
