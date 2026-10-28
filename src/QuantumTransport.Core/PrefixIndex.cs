namespace QuantumTransport.Core;

public sealed class PrefixIndex<T>
{
    private sealed class Node
    {
        internal Dictionary<char, Node> Children { get; } = [];
        internal bool HasValue;
        internal T Value = default!;
    }
    private readonly Node _root = new();
    public void Add(string prefix, T value)
    {
        if (string.IsNullOrEmpty(prefix)) throw new ArgumentException("Empty prefix");
        var node = _root;
        foreach (char c in prefix)
        {
            if (!node.Children.TryGetValue(c, out var child)) node.Children.Add(c, child = new());
            node = child;
        }
        node.Value = value; node.HasValue = true;
    }
    public bool TryMatch(string key, out T value, out int prefixLength)
    {
        var node = _root; bool found = false; value = default!; prefixLength = 0;
        for (int i = 0; i < key.Length && node.Children.TryGetValue(key[i], out node!); i++)
            if (node.HasValue) { found = true; value = node.Value; prefixLength = i + 1; }
        return found;
    }
}
