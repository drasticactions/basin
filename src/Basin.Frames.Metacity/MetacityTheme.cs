namespace Basin.Frames.Metacity;

/// <summary>
/// A loaded and validated metacity theme. It holds no Skia objects and does not change after a load.
/// </summary>
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

    /// <summary>
    /// Gets the theme name, by default the name of the folder above <c>metacity-1</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the folder that holds the theme file. Image paths in the theme resolve against it.
    /// </summary>
    public string Directory { get; }

    /// <summary>
    /// Gets the path of the theme file, or <see langword="null"/> for a theme parsed from a stream with no path.
    /// </summary>
    public string? FilePath { get; }

    /// <summary>
    /// Gets the format version, 1000 times the file's major number, raised by a satisfied <c>version</c> on the root element.
    /// </summary>
    public int FormatVersion { get; internal set; }

    /// <summary>
    /// Gets the theme's <c>info</c> element.
    /// </summary>
    public MetacityThemeInfo Info { get; internal set; }

    internal string? ReadableName { get; set; }

    internal string? Author { get; set; }

    internal string? Copyright { get; set; }

    internal string? Date { get; set; }

    internal string? Description { get; set; }

    internal MetacityFrameStyleSet?[] StyleSetsByType { get; } = new MetacityFrameStyleSet?[(int)MetacityFrameType.Count];

    internal List<MetacityColorSpec> ColorSpecs { get; } = [];

    internal IReadOnlyDictionary<string, MetacityImageInfo> Images => _images;

    /// <summary>
    /// Finds and loads a theme as marco does. It tries format 3, then 2, then 1, in each folder of <see cref="MetacityThemes.Directories"/>.
    /// A file whose root <c>version</c> is not satisfied continues the search.
    /// </summary>
    /// <param name="name">The theme name.</param>
    /// <returns>The theme.</returns>
    /// <exception cref="MetacityThemeException">No file loads, or a file has an error other than an unsatisfied root version.</exception>
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

    /// <summary>
    /// Loads one theme file. The major version comes from the file name.
    /// </summary>
    /// <param name="path">The path of a <c>metacity-theme-N.xml</c> file.</param>
    /// <param name="name">The theme name, or <see langword="null"/> for the name of the folder above the file's folder.</param>
    /// <returns>The theme.</returns>
    /// <exception cref="MetacityThemeException">The file is not a valid theme.</exception>
    public static MetacityTheme ParseFile(string path, string? name = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        name ??= Path.GetFileName(Path.GetDirectoryName(directory) ?? directory);
        using var stream = File.OpenRead(path);
        return Parse(stream, directory, MetacityThemes.MajorVersionOf(path), name, path);
    }

    /// <summary>
    /// Loads a theme from a stream. The stream is read to its end and disposed.
    /// </summary>
    /// <param name="stream">The theme XML.</param>
    /// <param name="directory">The folder that image paths resolve against.</param>
    /// <param name="majorVersion">The major format version of the file.</param>
    /// <param name="name">The theme name, or <see langword="null"/> for the name of the folder above <paramref name="directory"/>.</param>
    /// <param name="filePath">The path to report as <see cref="FilePath"/>.</param>
    /// <returns>The theme.</returns>
    /// <exception cref="MetacityThemeException">The text is not a valid theme.</exception>
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

    /// <summary>
    /// Gets a value indicating whether any frame style sets a window background with an alpha below 255.
    /// </summary>
    public bool HasTranslucentBackground
    {
        get
        {
            foreach (var style in _styles.Values)
            {
                if (style.WindowBackground is not null && style.WindowBackgroundAlpha < 255)
                {
                    return true;
                }
            }

            return false;
        }
    }

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
