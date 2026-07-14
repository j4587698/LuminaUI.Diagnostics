using Avalonia.Controls;

namespace LuminaUI.Diagnostics.Inspection;

public sealed class ResourceEntryRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ResourceEntryReference> _entries = new(StringComparer.Ordinal);

    public string Register(IResourceDictionary dictionary, object key)
    {
        var id = Guid.NewGuid().ToString("N");
        lock (_gate)
            _entries[id] = new ResourceEntryReference(dictionary, key);
        return id;
    }

    public bool TryGet(string id, out ResourceEntryReference entry)
    {
        lock (_gate)
            return _entries.TryGetValue(id, out entry!);
    }

    public void Clear()
    {
        lock (_gate)
            _entries.Clear();
    }
}

public sealed record ResourceEntryReference(IResourceDictionary Dictionary, object Key);
