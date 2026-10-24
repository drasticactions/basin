using System.Xml;
using Basin.Diagnostics;

namespace Basin.Freedesktop;

/// <summary>
/// Finds the MIME type of a file from its name with the shared-mime-info glob, icon and alias files. It never reads file contents.
/// The files are read on first use and cached until <see cref="Invalidate"/>. The class is not thread-safe.
/// </summary>
public sealed class MimeGlobs
{
    /// <summary>
    /// The MIME type that <see cref="TypeOf(string)"/> gives a directory.
    /// </summary>
    public const string Directory = "inode/directory";

    /// <summary>
    /// The MIME type that <see cref="TypeOf(string)"/> gives a name that no glob matches.
    /// </summary>
    public const string Unknown = "application/octet-stream";

    private static readonly BasinLogger Log = BasinLog.For("freedesktop");

    private readonly IReadOnlyList<string> _dataDirectories;
    private readonly List<MimeGlob> _literals = [];
    private readonly Dictionary<string, List<MimeGlob>> _suffixes = new(StringComparer.Ordinal);
    private readonly List<MimeGlob> _patterns = [];
    private readonly Dictionary<string, string> _icons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _genericIcons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _comments = new(StringComparer.Ordinal);
    private bool _loaded;

    /// <summary>
    /// Initializes a new instance of the <see cref="MimeGlobs"/> class. Nothing is read until the first lookup.
    /// </summary>
    /// <param name="dataDirectories">The data directories in order of importance, or <see langword="null"/> for <see cref="XdgDirectories.DataDirectories"/> as it is now.</param>
    public MimeGlobs(IReadOnlyList<string>? dataDirectories = null)
    {
        _dataDirectories = dataDirectories ?? XdgDirectories.DataDirectories();
    }

    /// <summary>
    /// Finds the MIME type of a path. An existing directory gives <see cref="Directory"/>, anything else is matched by its file name.
    /// </summary>
    /// <param name="path">The path to classify.</param>
    /// <returns>The canonical MIME type, or <see cref="Unknown"/> when no glob matches.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public string TypeOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (System.IO.Directory.Exists(path))
        {
            return Directory;
        }

        return TypeOfName(Path.GetFileName(path)) ?? Unknown;
    }

    /// <summary>
    /// Matches a file name against the globs: literal names first, then <c>*.ext</c> suffixes with the longest match winning, then full patterns.
    /// In one class a higher weight wins, then a longer pattern.
    /// </summary>
    /// <param name="fileName">The file name without a directory.</param>
    /// <returns>The canonical MIME type, or <see langword="null"/> when no glob matches.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fileName"/> is <see langword="null"/>.</exception>
    public string? TypeOfName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        Load();
        if (Best(_literals, g => Matches(g, fileName, literal: true)) is { } literal)
        {
            return Canonical(literal.MimeType);
        }

        MimeGlob? best = null;
        for (var dot = fileName.IndexOf('.'); dot >= 0; dot = fileName.IndexOf('.', dot + 1))
        {
            var suffix = fileName[(dot + 1)..].ToLowerInvariant();
            if (_suffixes.TryGetValue(suffix, out var candidates) &&
                Best(candidates, g => Matches(g, fileName, literal: false)) is { } found &&
                (best is not { } current || found.Weight > current.Weight || (found.Weight == current.Weight && found.Pattern.Length > current.Pattern.Length)))
            {
                best = found;
            }
        }

        if (best is { } suffixMatch)
        {
            return Canonical(suffixMatch.MimeType);
        }

        return Best(_patterns, g => Matches(g, fileName, literal: false)) is { } pattern ? Canonical(pattern.MimeType) : null;
    }

    /// <summary>
    /// Lists the icon names to try for a MIME type, after an alias resolves to its canonical type.
    /// </summary>
    /// <param name="mimeType">The MIME type.</param>
    /// <returns>The type's icon or its name with <c>/</c> replaced by <c>-</c>, then its generic icon, then <c>MEDIA-x-generic</c>. It is never empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mimeType"/> is <see langword="null"/>.</exception>
    public IReadOnlyList<string> IconNamesOf(string mimeType)
    {
        ArgumentNullException.ThrowIfNull(mimeType);
        Load();
        mimeType = Canonical(mimeType);
        var names = new List<string>(3);
        names.Add(_icons.TryGetValue(mimeType, out var icon) ? icon : mimeType.Replace('/', '-'));
        if (_genericIcons.TryGetValue(mimeType, out var generic))
        {
            names.Add(generic);
        }

        var slash = mimeType.IndexOf('/');
        var fallback = (slash > 0 ? mimeType[..slash] : mimeType) + "-x-generic";
        if (!names.Contains(fallback))
        {
            names.Add(fallback);
        }

        return names;
    }

    /// <summary>
    /// Reads the untranslated <c>comment</c> element from <c>mime/TYPE.xml</c> in the first data directory that has the file. The answer is cached.
    /// </summary>
    /// <param name="mimeType">The MIME type. An alias resolves to its canonical type.</param>
    /// <returns>The description, or <see langword="null"/> when no file has one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mimeType"/> is <see langword="null"/>.</exception>
    public string? CommentOf(string mimeType)
    {
        ArgumentNullException.ThrowIfNull(mimeType);
        Load();
        mimeType = Canonical(mimeType);
        if (_comments.TryGetValue(mimeType, out var cached))
        {
            return cached;
        }

        string? comment = null;
        foreach (var directory in _dataDirectories)
        {
            var file = Path.Combine(directory, "mime", mimeType + ".xml");
            if (File.Exists(file) && ReadComment(file) is { } found)
            {
                comment = found;
                break;
            }
        }

        _comments[mimeType] = comment;
        return comment;
    }

    /// <summary>
    /// Drops every cached glob, icon, alias and comment. The next lookup reads the files again.
    /// </summary>
    public void Invalidate()
    {
        _loaded = false;
        _literals.Clear();
        _suffixes.Clear();
        _patterns.Clear();
        _icons.Clear();
        _genericIcons.Clear();
        _aliases.Clear();
        _comments.Clear();
    }

    private string Canonical(string mimeType) => _aliases.TryGetValue(mimeType, out var canonical) ? canonical : mimeType;

    private static MimeGlob? Best(List<MimeGlob> globs, Func<MimeGlob, bool> matches)
    {
        MimeGlob? best = null;
        foreach (var glob in globs)
        {
            if (!matches(glob))
            {
                continue;
            }

            if (best is not { } current || glob.Weight > current.Weight ||
                (glob.Weight == current.Weight && glob.Pattern.Length > current.Pattern.Length))
            {
                best = glob;
            }
        }

        return best;
    }

    private static bool Matches(MimeGlob glob, string name, bool literal)
    {
        var comparison = glob.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (literal)
        {
            return string.Equals(glob.Pattern, name, comparison);
        }

        return glob.Pattern.StartsWith("*.", StringComparison.Ordinal) && glob.Pattern.IndexOfAny(['*', '?', '['], 1) < 0
            ? name.EndsWith(glob.Pattern[1..], comparison)
            : GlobMatch(glob.Pattern, name, glob.CaseSensitive);
    }

    private static bool GlobMatch(string pattern, string name, bool caseSensitive)
    {
        if (!caseSensitive)
        {
            pattern = pattern.ToLowerInvariant();
            name = name.ToLowerInvariant();
        }

        return GlobMatch(pattern.AsSpan(), name.AsSpan());
    }

    private static bool GlobMatch(ReadOnlySpan<char> pattern, ReadOnlySpan<char> name)
    {
        while (pattern.Length > 0)
        {
            switch (pattern[0])
            {
                case '*':
                    for (var skip = 0; skip <= name.Length; skip++)
                    {
                        if (GlobMatch(pattern[1..], name[skip..]))
                        {
                            return true;
                        }
                    }

                    return false;
                case '?':
                    if (name.IsEmpty)
                    {
                        return false;
                    }

                    pattern = pattern[1..];
                    name = name[1..];
                    break;
                case '[':
                    var close = pattern.IndexOf(']');
                    if (close < 0 || name.IsEmpty)
                    {
                        return false;
                    }

                    var set = pattern[1..close];
                    var negate = set.Length > 0 && set[0] == '!';
                    if (negate)
                    {
                        set = set[1..];
                    }

                    var hit = false;
                    for (var i = 0; i < set.Length; i++)
                    {
                        if (i + 2 < set.Length && set[i + 1] == '-')
                        {
                            hit |= name[0] >= set[i] && name[0] <= set[i + 2];
                            i += 2;
                        }
                        else
                        {
                            hit |= name[0] == set[i];
                        }
                    }

                    if (hit == negate)
                    {
                        return false;
                    }

                    pattern = pattern[(close + 1)..];
                    name = name[1..];
                    break;
                default:
                    if (name.IsEmpty || name[0] != pattern[0])
                    {
                        return false;
                    }

                    pattern = pattern[1..];
                    name = name[1..];
                    break;
            }
        }

        return name.IsEmpty;
    }

    private void Load()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        var globs = new Dictionary<string, List<MimeGlob>>(StringComparer.Ordinal);
        for (var i = _dataDirectories.Count - 1; i >= 0; i--)
        {
            var mime = Path.Combine(_dataDirectories[i], "mime");
            ReadGlobs(Path.Combine(mime, "globs2"), globs);
            ReadPairs(Path.Combine(mime, "icons"), _icons);
            ReadPairs(Path.Combine(mime, "generic-icons"), _genericIcons);
            ReadPairs(Path.Combine(mime, "aliases"), _aliases, spaced: true);
        }

        foreach (var list in globs.Values)
        {
            foreach (var glob in list)
            {
                if (glob.Pattern.IndexOfAny(['*', '?', '[']) < 0)
                {
                    _literals.Add(glob);
                }
                else if (glob.Pattern.StartsWith("*.", StringComparison.Ordinal) && glob.Pattern.IndexOfAny(['*', '?', '['], 1) < 0)
                {
                    var suffix = glob.Pattern[2..].ToLowerInvariant();
                    if (!_suffixes.TryGetValue(suffix, out var bucket))
                    {
                        bucket = [];
                        _suffixes[suffix] = bucket;
                    }

                    bucket.Add(glob);
                }
                else
                {
                    _patterns.Add(glob);
                }
            }
        }
    }

    private static void ReadGlobs(string file, Dictionary<string, List<MimeGlob>> globs)
    {
        if (!File.Exists(file))
        {
            return;
        }

        foreach (var line in File.ReadLines(file))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var fields = line.Split(':');
            if (fields.Length < 3 || !int.TryParse(fields[0], out var weight))
            {
                continue;
            }

            var type = fields[1];
            var pattern = fields[2];
            if (pattern == "__NOGLOBS__")
            {
                globs.Remove(type);
                continue;
            }

            var caseSensitive = fields.Length > 3 && fields[3].Split(',').Contains("cs");
            if (!globs.TryGetValue(type, out var list))
            {
                list = [];
                globs[type] = list;
            }

            list.Add(new MimeGlob(weight, type, pattern, caseSensitive));
        }
    }

    private static void ReadPairs(string file, Dictionary<string, string> table, bool spaced = false)
    {
        if (!File.Exists(file))
        {
            return;
        }

        foreach (var line in File.ReadLines(file))
        {
            var split = spaced ? line.IndexOf(' ') : line.IndexOf(':');
            if (split > 0 && line[0] != '#')
            {
                table[line[..split]] = line[(split + 1)..].Trim();
            }
        }
    }

    private static string? ReadComment(string file)
    {
        try
        {
            using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "comment" && reader.GetAttribute("xml:lang") is null)
                {
                    return reader.ReadElementContentAsString();
                }
            }
        }
        catch (Exception error) when (error is XmlException or IOException or UnauthorizedAccessException)
        {
            Log.Debug(error, $"{file}: comment not read");
        }

        return null;
    }
}
