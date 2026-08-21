namespace Basin;

public sealed class DrmFormatSet
{
    public const ulong ModifierInvalid = 0x00ffffffffffffff;

    public const ulong ModifierLinear = 0;

    public static readonly DrmFormatSet Empty = new();

    private readonly Dictionary<DrmFormat, HashSet<ulong>> _formats = [];

    public IReadOnlyCollection<DrmFormat> Formats => _formats.Keys;

    public int Count => _formats.Count;

    public void Add(DrmFormat format, ulong modifier)
    {
        if (!_formats.TryGetValue(format, out var modifiers))
        {
            _formats[format] = modifiers = [];
        }

        modifiers.Add(modifier);
    }

    public void Add(DrmFormat format, ReadOnlySpan<ulong> modifiers)
    {
        foreach (var modifier in modifiers)
        {
            Add(format, modifier);
        }
    }

    public bool Contains(DrmFormat format) => _formats.ContainsKey(format);

    public bool Contains(DrmFormat format, ulong modifier) =>
        _formats.TryGetValue(format, out var modifiers) && modifiers.Contains(modifier);

    public IReadOnlyCollection<ulong> ModifiersOf(DrmFormat format) =>
        _formats.TryGetValue(format, out var modifiers) ? modifiers : [];

    public DrmFormatSet Union(DrmFormatSet other)
    {
        var result = new DrmFormatSet();
        foreach (var (format, modifiers) in _formats)
        {
            foreach (var modifier in modifiers)
            {
                result.Add(format, modifier);
            }
        }

        foreach (var (format, modifiers) in other._formats)
        {
            foreach (var modifier in modifiers)
            {
                result.Add(format, modifier);
            }
        }

        return result;
    }

    public DrmFormatSet Intersect(DrmFormatSet other)
    {
        var result = new DrmFormatSet();
        foreach (var (format, modifiers) in _formats)
        {
            if (!other._formats.TryGetValue(format, out var theirs))
            {
                continue;
            }

            foreach (var modifier in modifiers)
            {
                if (theirs.Contains(modifier))
                {
                    result.Add(format, modifier);
                }
            }
        }

        return result;
    }
}
