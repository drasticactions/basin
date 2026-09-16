namespace Basin.Frames.Metacity;

public sealed class MetacityTheme
{
    private readonly Dictionary<string, int> _intConstants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _floatConstants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _colorConstants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MetacityFrameLayout> _layouts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MetacityDrawOpList> _drawOpLists = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MetacityFrameStyle> _styles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MetacityFrameStyleSet> _styleSets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MetacityImageInfo> _images = new(StringComparer.Ordinal);

    internal MetacityTheme(string name, string directory, string? filePath, int formatVersion)
    {
        Name = name;
        Directory = directory;
        FilePath = filePath;
        FormatVersion = formatVersion;
    }

    public string Name { get; }

    public string Directory { get; }

    public string? FilePath { get; }

    public int FormatVersion { get; internal set; }

    public MetacityThemeInfo Info { get; internal set; }

    internal string? ReadableName { get; set; }

    internal string? Author { get; set; }

    internal string? Copyright { get; set; }

    internal string? Date { get; set; }

    internal string? Description { get; set; }

    internal MetacityFrameStyleSet?[] StyleSetsByType { get; } = new MetacityFrameStyleSet?[(int)MetacityFrameType.Count];

    internal List<MetacityColorSpec> ColorSpecs { get; } = [];

    internal IReadOnlyDictionary<string, MetacityImageInfo> Images => _images;

    public static MetacityTheme Load(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var directories = MetacityThemes.Directories(name);
        for (var major = MetacityThemes.MajorVersion; major > 0; major--)
        {
            foreach (var directory in directories)
            {
                var path = Path.Combine(directory, MetacityThemes.FileName(major));
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    return ParseFile(path, name);
                }
                catch (MetacityThemeException e) when (e.TooOld)
                {
                }
            }
        }

        throw new MetacityThemeException(
            $"Failed to find a valid file for theme {name}. Searched: {string.Join(", ", directories)}");
    }

    public static MetacityTheme ParseFile(string path, string? name = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        name ??= Path.GetFileName(Path.GetDirectoryName(directory) ?? directory);
        using var stream = File.OpenRead(path);
        return Parse(stream, directory, MetacityThemes.MajorVersionOf(path), name, path);
    }

    public static MetacityTheme Parse(Stream stream, string directory, int majorVersion = 1, string? name = null, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(directory);
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        return MetacityThemeReader.Parse(text, name ?? Path.GetFileName(Path.GetDirectoryName(directory) ?? directory), directory, filePath, majorVersion);
    }

    internal bool TryGetIntConstant(string name, out int value) => _intConstants.TryGetValue(name, out value);

    internal bool TryGetFloatConstant(string name, out double value) => _floatConstants.TryGetValue(name, out value);

    internal bool TryGetColorConstant(string name, out string value) => _colorConstants.TryGetValue(name!, out value!);

    internal string? DefineIntConstant(string name, int value) => Define(_intConstants, name, value);

    internal string? DefineFloatConstant(string name, double value) => Define(_floatConstants, name, value);

    internal string? DefineColorConstant(string name, string value) => Define(_colorConstants, name, value);

    internal MetacityFrameLayout? LookupLayout(string name) => _layouts.GetValueOrDefault(name);

    internal void InsertLayout(string name, MetacityFrameLayout layout) => _layouts[name] = layout;

    internal MetacityDrawOpList? LookupDrawOpList(string name) => _drawOpLists.GetValueOrDefault(name);

    internal void InsertDrawOpList(string name, MetacityDrawOpList list) => _drawOpLists[name] = list;

    internal MetacityFrameStyle? LookupStyle(string name) => _styles.GetValueOrDefault(name);

    internal void InsertStyle(string name, MetacityFrameStyle style) => _styles[name] = style;

    internal MetacityFrameStyleSet? LookupStyleSet(string name) => _styleSets.GetValueOrDefault(name);

    internal void InsertStyleSet(string name, MetacityFrameStyleSet set) => _styleSets[name] = set;

    internal MetacityImageInfo? LoadImage(string filename, out string? error)
    {
        error = null;
        if (_images.TryGetValue(filename, out var cached))
        {
            return cached;
        }

        var info = MetacityImageProbe.Probe(Directory, filename, out error);
        if (info is not null)
        {
            _images[filename] = info;
        }

        return info;
    }

    internal MetacityFrameStyle? GetStyle(MetacityFrameType type, MetacityFrameStateKind state, MetacityResize resize, MetacityFocus focus)
    {
        var set = StyleSetsByType[(int)type];
        if (set is null && type == MetacityFrameType.Attached)
        {
            set = StyleSetsByType[(int)MetacityFrameType.Border];
        }

        set ??= StyleSetsByType[(int)MetacityFrameType.Normal];
        return set?.GetStyle(state, resize, focus);
    }

    internal string? Validate()
    {
        if (ReadableName is null)
        {
            return $"No <name> set for theme \"{Name}\"";
        }

        if (Author is null)
        {
            return $"No <author> set for theme \"{Name}\"";
        }

        if (Date is null)
        {
            return $"No <date> set for theme \"{Name}\"";
        }

        if (Description is null)
        {
            return $"No <description> set for theme \"{Name}\"";
        }

        if (Copyright is null)
        {
            return $"No <copyright> set for theme \"{Name}\"";
        }

        for (var type = MetacityFrameType.Normal; type < MetacityFrameType.Count; type++)
        {
            if (type != MetacityFrameType.Attached && StyleSetsByType[(int)type] is null)
            {
                var typeName = MetacityThemeReader.FrameTypeName(type);
                return $"No frame style set for window type \"{typeName}\" in theme \"{Name}\", add a <window type=\"{typeName}\" style_set=\"whatever\"/> element";
            }
        }

        Info = new MetacityThemeInfo(ReadableName, Author, Copyright, Date, Description);
        return null;
    }

    private static string? Define<T>(Dictionary<string, T> table, string name, T value)
    {
        if (name.Length == 0 || !char.IsAsciiLetterUpper(name[0]))
        {
            return $"User-defined constants must begin with a capital letter; \"{name}\" does not";
        }

        if (!table.TryAdd(name, value))
        {
            return $"Constant \"{name}\" has already been defined";
        }

        return null;
    }
}
