namespace Basin.Freedesktop;

internal sealed class KeyFileGroup(string name)
{
    public string Name { get; } = name;

    public List<KeyFileEntry> Entries { get; } = [];

    public string? Get(string key)
    {
        foreach (var entry in Entries)
        {
            if (entry.Suffix is null && entry.Key == key)
            {
                return entry.Value;
            }
        }

        return null;
    }
}
