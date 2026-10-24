using System.Globalization;
using System.Text;

namespace Basin.Freedesktop;

/// <summary>
/// The home trash of the freedesktop.org Trash specification. It moves files in and out and keeps the <c>directorysizes</c> cache.
/// </summary>
public sealed class FreedesktopTrash
{
    private const string InfoExtension = ".trashinfo";

    /// <summary>
    /// Initializes a new instance of the <see cref="FreedesktopTrash"/> class. No directory is created until the first <see cref="Trash(string, DateTime?)"/>.
    /// </summary>
    /// <param name="root">The trash directory, or <see langword="null"/> for <c>Trash</c> under <see cref="XdgDirectories.DataHome"/>.</param>
    public FreedesktopTrash(string? root = null)
    {
        Root = root ?? Path.Combine(XdgDirectories.DataHome, "Trash");
    }

    /// <summary>
    /// Gets the trash directory that holds <c>files</c>, <c>info</c> and <c>directorysizes</c>.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// Gets the directory that holds the trashed files and directories.
    /// </summary>
    public string FilesDirectory => Path.Combine(Root, "files");

    /// <summary>
    /// Gets the directory that holds one <c>.trashinfo</c> file per item.
    /// </summary>
    public string InfoDirectory => Path.Combine(Root, "info");

    private string SizesFile => Path.Combine(Root, "directorysizes");

    /// <summary>
    /// Gets a value indicating whether the info directory holds no <c>.trashinfo</c> file. Each read asks the file system.
    /// </summary>
    public bool IsEmpty =>
        !Directory.Exists(InfoDirectory) || !Directory.EnumerateFiles(InfoDirectory, "*" + InfoExtension).Any();

    /// <summary>
    /// Moves a file or directory into the trash. The info file is created exclusively, so two processes cannot take the same name.
    /// A name that is taken gets a number, for example <c>notes.2.txt</c>. When the move fails, the info file is removed again.
    /// </summary>
    /// <param name="path">The file or directory to move. A relative path is resolved against the current directory.</param>
    /// <param name="now">The deletion time to record, or <see langword="null"/> for the local time now.</param>
    /// <returns>The new item.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or empty.</exception>
    /// <exception cref="FileNotFoundException">Nothing exists at <paramref name="path"/>.</exception>
    public TrashItem Trash(string path, DateTime? now = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var full = Path.GetFullPath(path);
        if (!File.Exists(full) && !Directory.Exists(full))
        {
            throw new FileNotFoundException("nothing to move to the Trash", full);
        }

        EnsureDirectories();
        var deleted = now ?? DateTime.Now;
        var (name, info) = ReserveName(Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar)));
        var target = Path.Combine(FilesDirectory, name);
        try
        {
            File.WriteAllText(info, $"[Trash Info]\nPath={Encode(full)}\nDeletionDate={deleted.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)}\n");
            Move(full, target);
        }
        catch
        {
            File.Delete(info);
            throw;
        }

        if (Directory.Exists(target))
        {
            AppendDirectorySize(name, target);
        }

        return new TrashItem(name, full, deleted, target, info);
    }

    /// <summary>
    /// Lists the items in the trash. An info file whose file is gone, or which does not parse, is skipped.
    /// </summary>
    /// <returns>A new list, newest deletion first.</returns>
    public IReadOnlyList<TrashItem> Items()
    {
        var items = new List<TrashItem>();
        if (!Directory.Exists(InfoDirectory))
        {
            return items;
        }

        foreach (var info in Directory.EnumerateFiles(InfoDirectory, "*" + InfoExtension))
        {
            var name = Path.GetFileName(info)[..^InfoExtension.Length];
            var files = Path.Combine(FilesDirectory, name);
            if (!File.Exists(files) && !Directory.Exists(files))
            {
                continue;
            }

            if (ReadInfo(info) is { } parsed)
            {
                items.Add(new TrashItem(name, parsed.Path, parsed.Deleted, files, info));
            }
        }

        items.Sort((a, b) => b.DeletedAt.CompareTo(a.DeletedAt));
        return items;
    }

    /// <summary>
    /// Counts the items and adds up their sizes. A directory size comes from <c>directorysizes</c> when its line is current, otherwise it is computed.
    /// </summary>
    /// <returns>The item count and the total size in bytes.</returns>
    public TrashSummary Summary()
    {
        var sizes = ReadDirectorySizes();
        long bytes = 0;
        var count = 0;
        foreach (var item in Items())
        {
            count++;
            if (item.IsDirectory)
            {
                var stamp = MTime(item.InfoPath);
                bytes += sizes.TryGetValue(item.Name, out var cached) && cached.MTime == stamp ? cached.Size : SizeOf(item.FilesPath);
            }
            else
            {
                bytes += new FileInfo(item.FilesPath).Length;
            }
        }

        return new TrashSummary(count, bytes);
    }

    /// <summary>
    /// Moves an item back to its original path and removes its info file and its size line. Missing parent directories are created.
    /// </summary>
    /// <param name="item">The item to restore.</param>
    /// <returns>The original path that the item now has again.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    /// <exception cref="IOException">Something already exists at the original path.</exception>
    public string Restore(TrashItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (File.Exists(item.OriginalPath) || Directory.Exists(item.OriginalPath))
        {
            throw new IOException($"{item.OriginalPath} already exists");
        }

        if (Path.GetDirectoryName(item.OriginalPath) is { Length: > 0 } parent)
        {
            Directory.CreateDirectory(parent);
        }

        Move(item.FilesPath, item.OriginalPath);
        File.Delete(item.InfoPath);
        RemoveDirectorySize(item.Name);
        return item.OriginalPath;
    }

    /// <summary>
    /// Deletes one item permanently, with its info file and its size line.
    /// </summary>
    /// <param name="item">The item to delete.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    public void Delete(TrashItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        DeletePath(item.FilesPath);
        File.Delete(item.InfoPath);
        RemoveDirectorySize(item.Name);
    }

    /// <summary>
    /// Deletes every item that <see cref="Items"/> lists, with its info file, and deletes the <c>directorysizes</c> file.
    /// </summary>
    public void Empty()
    {
        foreach (var item in Items())
        {
            DeletePath(item.FilesPath);
            File.Delete(item.InfoPath);
        }

        if (File.Exists(SizesFile))
        {
            File.Delete(SizesFile);
        }
    }

    /// <summary>
    /// Measures a file, or a directory recursively. Symbolic links and entries that cannot be read are skipped.
    /// </summary>
    /// <param name="path">The file or directory to measure.</param>
    /// <returns>The total size in bytes.</returns>
    public static long SizeOf(string path)
    {
        if (File.Exists(path))
        {
            return new FileInfo(path).Length;
        }

        long total = 0;
        foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true }))
        {
            total += file.Length;
        }

        return total;
    }

    internal static string Encode(string path)
    {
        var builder = new StringBuilder(path.Length);
        foreach (var b in Encoding.UTF8.GetBytes(path))
        {
            var c = (char)b;
            if (b < 0x80 && (char.IsAsciiLetterOrDigit(c) || "/-_.~!*'()".Contains(c)))
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    internal static string Decode(string value) => Uri.UnescapeDataString(value);

    private void EnsureDirectories()
    {
        foreach (var directory in (string[])[Root, FilesDirectory, InfoDirectory])
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }

    private (string Name, string Info) ReserveName(string baseName)
    {
        var stem = Path.GetFileNameWithoutExtension(baseName);
        var extension = Path.GetExtension(baseName);
        for (var n = 1; ; n++)
        {
            var name = n == 1 ? baseName : $"{stem}.{n}{extension}";
            var info = Path.Combine(InfoDirectory, name + InfoExtension);
            if (File.Exists(Path.Combine(FilesDirectory, name)) || Directory.Exists(Path.Combine(FilesDirectory, name)))
            {
                continue;
            }

            try
            {
                using (new FileStream(info, FileMode.CreateNew, FileAccess.Write))
                {
                }

                return (name, info);
            }
            catch (IOException) when (File.Exists(info))
            {
            }
        }
    }

    private static void Move(string from, string to)
    {
        try
        {
            if (Directory.Exists(from))
            {
                Directory.Move(from, to);
            }
            else
            {
                File.Move(from, to);
            }
        }
        catch (IOException) when (Directory.Exists(from))
        {
            CopyDirectory(from, to);
            Directory.Delete(from, recursive: true);
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.EnumerateDirectories(from))
        {
            CopyDirectory(directory, Path.Combine(to, Path.GetFileName(directory)));
        }
    }

    private static void DeletePath(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static (string Path, DateTime Deleted)? ReadInfo(string file)
    {
        string? path = null;
        var deleted = DateTime.MinValue;
        var inGroup = false;
        foreach (var raw in File.ReadLines(file))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inGroup = line == "[Trash Info]";
                continue;
            }

            if (!inGroup)
            {
                continue;
            }

            if (line.StartsWith("Path=", StringComparison.Ordinal))
            {
                path = Decode(line[5..]);
            }
            else if (line.StartsWith("DeletionDate=", StringComparison.Ordinal))
            {
                _ = DateTime.TryParseExact(line[13..], "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out deleted);
            }
        }

        return path is null ? null : (path, deleted);
    }

    private static long MTime(string path) => new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds();

    private Dictionary<string, (long Size, long MTime)> ReadDirectorySizes()
    {
        var sizes = new Dictionary<string, (long, long)>(StringComparer.Ordinal);
        if (!File.Exists(SizesFile))
        {
            return sizes;
        }

        foreach (var line in File.ReadLines(SizesFile))
        {
            var fields = line.Split(' ', 3);
            if (fields.Length == 3 && long.TryParse(fields[0], out var size) && long.TryParse(fields[1], out var mtime))
            {
                sizes[Decode(fields[2])] = (size, mtime);
            }
        }

        return sizes;
    }

    private void AppendDirectorySize(string name, string target)
    {
        var info = Path.Combine(InfoDirectory, name + InfoExtension);
        File.AppendAllText(SizesFile, $"{SizeOf(target)} {MTime(info)} {Encode(name)}\n");
    }

    private void RemoveDirectorySize(string name)
    {
        if (!File.Exists(SizesFile))
        {
            return;
        }

        var encoded = Encode(name);
        var kept = File.ReadLines(SizesFile).Where(line => line.Split(' ', 3) is not [_, _, var entry] || entry != encoded).ToList();
        var temporary = SizesFile + ".tmp";
        File.WriteAllLines(temporary, kept);
        File.Move(temporary, SizesFile, overwrite: true);
    }
}
