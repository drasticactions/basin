using Basin.Diagnostics;

namespace Basin.Freedesktop;

public sealed class IconTheme
{
    private static readonly BasinLogger Log = BasinLog.For("freedesktop");

    private IconTheme(string name, IReadOnlyList<string> baseDirectories, bool hasIndex, IReadOnlyList<string> inherits, IReadOnlyList<IconThemeDirectory> directories)
    {
        Name = name;
        BaseDirectories = baseDirectories;
        HasIndex = hasIndex;
        Inherits = inherits;
        Directories = directories;
    }

    public string Name { get; }

    public IReadOnlyList<string> BaseDirectories { get; }

    public bool HasIndex { get; }

    public IReadOnlyList<string> Inherits { get; }

    public IReadOnlyList<IconThemeDirectory> Directories { get; }

    public static IconTheme? Load(string name, IReadOnlyList<string> dataDirectories)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(dataDirectories);
        var bases = new List<string>();
        List<KeyFileGroup>? index = null;
        foreach (var dataDirectory in dataDirectories)
        {
            var path = Path.Combine(dataDirectory, "icons", name);
            if (!Directory.Exists(path))
            {
                continue;
            }

            bases.Add(path);
            var indexPath = Path.Combine(path, "index.theme");
            if (index is null && File.Exists(indexPath))
            {
                index = KeyFile.Read(indexPath);
            }
        }

        if (bases.Count == 0)
        {
            return null;
        }

        if (index is null)
        {
            return new IconTheme(name, bases, false, [], []);
        }

        KeyFileGroup? header = null;
        foreach (var group in index)
        {
            if (group.Name == "Icon Theme")
            {
                header = group;
                break;
            }
        }

        if (header is null)
        {
            Log.Debug($"{name}: index.theme has no [Icon Theme] group, walked as a bare tree");
            return new IconTheme(name, bases, false, [], []);
        }

        var inherits = SplitList(header.Get("Inherits"));
        var directories = new List<IconThemeDirectory>();
        foreach (var listed in SplitList(header.Get("Directories")))
        {
            AddDirectory(index, listed, directories, name, bases);
        }

        foreach (var listed in SplitList(header.Get("ScaledDirectories")))
        {
            AddDirectory(index, listed, directories, name, bases);
        }

        return new IconTheme(name, bases, true, inherits, directories);
    }

    private static void AddDirectory(List<KeyFileGroup> index, string listed, List<IconThemeDirectory> directories, string theme, List<string> bases)
    {
        var present = false;
        foreach (var baseDirectory in bases)
        {
            if (Directory.Exists(Path.Combine(baseDirectory, listed)))
            {
                present = true;
                break;
            }
        }

        if (!present)
        {
            return;
        }

        KeyFileGroup? group = null;
        foreach (var candidate in index)
        {
            if (candidate.Name == listed)
            {
                group = candidate;
                break;
            }
        }

        if (group is null)
        {
            Log.Debug($"{theme}: index.theme lists {listed} and has no [{listed}] group, skipped");
            return;
        }

        if (!int.TryParse(group.Get("Size"), out var size) || size <= 0)
        {
            Log.Debug($"{theme}: [{listed}] has no Size, skipped");
            return;
        }

        var scale = int.TryParse(group.Get("Scale"), out var parsedScale) && parsedScale > 0 ? parsedScale : 1;
        var type = group.Get("Type") switch
        {
            "Fixed" => IconThemeDirectoryType.Fixed,
            "Scalable" => IconThemeDirectoryType.Scaled,
            _ => IconThemeDirectoryType.Threshold,
        };
        var minSize = int.TryParse(group.Get("MinSize"), out var parsedMin) && parsedMin > 0 ? parsedMin : size;
        var maxSize = int.TryParse(group.Get("MaxSize"), out var parsedMax) && parsedMax > 0 ? parsedMax : size;
        var threshold = int.TryParse(group.Get("Threshold"), out var parsedThreshold) && parsedThreshold >= 0 ? parsedThreshold : 2;
        directories.Add(new IconThemeDirectory(listed, size, scale, type, minSize, maxSize, threshold, group.Get("Context")));
    }

    private static List<string> SplitList(string? value)
    {
        var items = new List<string>();
        if (string.IsNullOrEmpty(value))
        {
            return items;
        }

        foreach (var piece in value.Split(','))
        {
            var trimmed = piece.Trim();
            if (trimmed.Length > 0)
            {
                items.Add(trimmed);
            }
        }

        return items;
    }
}
