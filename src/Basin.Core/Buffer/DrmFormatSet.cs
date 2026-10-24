namespace Basin;

/// <summary>
/// A set of DRM formats, each with the set of modifiers that it supports.
/// </summary>
public sealed class DrmFormatSet
{
    /// <summary>The modifier value that means "implicit modifier", as in <c>DRM_FORMAT_MOD_INVALID</c>.</summary>
    public const ulong ModifierInvalid = 0x00ffffffffffffff;

    /// <summary>The modifier value for a linear layout, as in <c>DRM_FORMAT_MOD_LINEAR</c>.</summary>
    public const ulong ModifierLinear = 0;

    /// <summary>A shared empty set that cannot change. <see cref="Add(DrmFormat, ulong)"/> on it throws.</summary>
    public static readonly DrmFormatSet Empty = new(readOnly: true);

    private readonly Dictionary<DrmFormat, HashSet<ulong>> _formats = [];
    private readonly bool _readOnly;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrmFormatSet"/> class that is empty and writable.
    /// </summary>
    public DrmFormatSet()
    {
    }

    private DrmFormatSet(bool readOnly)
    {
        _readOnly = readOnly;
    }

    /// <summary>Gets a value indicating whether the set refuses <see cref="Add(DrmFormat, ulong)"/>. Only <see cref="Empty"/> is read-only.</summary>
    public bool IsReadOnly => _readOnly;

    /// <summary>Gets the formats in the set.</summary>
    public IReadOnlyCollection<DrmFormat> Formats => _formats.Keys;

    /// <summary>Gets the number of formats in the set.</summary>
    public int Count => _formats.Count;

    /// <summary>Adds one format and modifier pair.</summary>
    /// <param name="format">The format.</param>
    /// <param name="modifier">The modifier.</param>
    /// <exception cref="InvalidOperationException">The set is read-only.</exception>
    public void Add(DrmFormat format, ulong modifier)
    {
        if (_readOnly)
        {
            throw new InvalidOperationException("DrmFormatSet.Empty is shared and cannot change.");
        }

        if (!_formats.TryGetValue(format, out var modifiers))
        {
            _formats[format] = modifiers = [];
        }

        modifiers.Add(modifier);
    }

    /// <summary>Adds a format with each of several modifiers.</summary>
    /// <param name="format">The format.</param>
    /// <param name="modifiers">The modifiers to add.</param>
    /// <exception cref="InvalidOperationException">The set is read-only.</exception>
    public void Add(DrmFormat format, ReadOnlySpan<ulong> modifiers)
    {
        if (_readOnly)
        {
            throw new InvalidOperationException("DrmFormatSet.Empty is shared and cannot change.");
        }

        foreach (var modifier in modifiers)
        {
            Add(format, modifier);
        }
    }

    /// <summary>Tests whether the set holds a format with any modifier.</summary>
    /// <param name="format">The format.</param>
    /// <returns><see langword="true"/> when the format is in the set.</returns>
    public bool Contains(DrmFormat format) => _formats.ContainsKey(format);

    /// <summary>Tests whether the set holds one format and modifier pair.</summary>
    /// <param name="format">The format.</param>
    /// <param name="modifier">The modifier.</param>
    /// <returns><see langword="true"/> when the pair is in the set.</returns>
    public bool Contains(DrmFormat format, ulong modifier) =>
        _formats.TryGetValue(format, out var modifiers) && modifiers.Contains(modifier);

    /// <summary>Gets the modifiers of one format.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The live modifier set, or an empty collection when the format is absent.</returns>
    public IReadOnlyCollection<ulong> ModifiersOf(DrmFormat format) =>
        _formats.TryGetValue(format, out var modifiers) ? modifiers : [];

    /// <summary>Makes a new set with each pair from this set and from <paramref name="other"/>.</summary>
    /// <param name="other">The second set.</param>
    /// <returns>A new set. Neither input changes.</returns>
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

    /// <summary>Makes a new set with each pair that this set and <paramref name="other"/> share.</summary>
    /// <param name="other">The second set.</param>
    /// <returns>A new set. Neither input changes.</returns>
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
