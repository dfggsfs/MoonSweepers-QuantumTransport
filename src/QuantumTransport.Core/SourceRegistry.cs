namespace QuantumTransport.Core;

public readonly record struct SourceIdentity(string Owner, string Kind, int MachineId = -1, string? Controller = null);

/// <summary>Resolve explicit game-owned storage registrations; ambiguous owners are never guessed.</summary>
public sealed class SourceRegistry
{
    private readonly Dictionary<string, SourceIdentity?> _entries = new(StringComparer.Ordinal);
    public static bool ValidOwner(string? owner) => !string.IsNullOrWhiteSpace(owner) && owner.Length <= 128;
    public static bool IsStoredContainerSource(string? container, string? loadedContainer) =>
        !string.IsNullOrEmpty(container) && !string.Equals(container, loadedContainer, StringComparison.Ordinal);
    public static bool UseFarRobotPayload(bool nearViewPresent, double confirmedKilograms) =>
        !nearViewPresent && double.IsFinite(confirmedKilograms) && confirmedKilograms > FlowPlanner.Epsilon;

    public void Register(string? storage, SourceIdentity identity)
    {
        if (string.IsNullOrEmpty(storage) || !ValidOwner(identity.Owner)) return;
        if (!_entries.TryGetValue(storage, out var previous)) _entries.Add(storage, identity);
        else if (previous != identity) _entries[storage] = null;
    }

    public bool TryResolve(string? storage, out SourceIdentity identity)
    {
        if (!string.IsNullOrEmpty(storage) && _entries.TryGetValue(storage, out var value) && value.HasValue)
        { identity = value.Value; return true; }
        identity = default;
        return false;
    }
}
