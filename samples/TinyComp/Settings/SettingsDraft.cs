using System.Globalization;
using Basin.Config;
using Tomlyn;
using Tomlyn.Model;

namespace TinyComp;

internal sealed class SettingsDraft
{
    private readonly HashSet<string> _dirty = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _errors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _hints = new(StringComparer.Ordinal);
    private TomlDocument _file;
    private TomlDocument _draft;
    private TomlTable _fileModel;
    private TomlTable _draftModel;
    private long _fileLength;
    private DateTime _fileWrite;

    private SettingsDraft(string? path, TomlDocument file, string? refusal)
    {
        FilePath = path;
        Refusal = refusal;
        _file = file;
        _draft = TomlDocument.Parse(file.ToString());
        _fileModel = Toml.ToModel(file.ToString());
        _draftModel = _fileModel;
        Stamp();
    }

    public string? FilePath { get; }

    public string? Refusal { get; private set; }

    public string? SaveBlocked => FilePath is null
        ? "--config false reads no file, so there is nothing to save"
        : Refusal;

    public TomlDocument Document => _draft;

    public string Text => _draft.ToString();

    public string FileText => _file.ToString();

    public int DirtyCount => _dirty.Count;

    public IReadOnlyCollection<string> DirtyPaths => _dirty;

    public string Status { get; set; } = string.Empty;

    public bool Conflict { get; private set; }

    public int Version { get; private set; }

    public string? LastPath { get; private set; }

    public event Action? Changed;

    public static SettingsDraft Open(string? path)
    {
        if (path is null)
        {
            return new SettingsDraft(null, TomlDocument.Parse(string.Empty), null);
        }

        if (!File.Exists(path))
        {
            return new SettingsDraft(path, TomlDocument.Parse(string.Empty), null);
        }

        try
        {
            return new SettingsDraft(path, TomlDocument.Load(path), null);
        }
        catch (Exception error) when (error is FormatException or IOException or UnauthorizedAccessException)
        {
            return new SettingsDraft(path, TomlDocument.Parse(string.Empty), $"{path} does not read: {error.Message}");
        }
    }

    public object? Get(string table, string key) => Lookup(_draftModel, table, key);

    public object? FileValue(string table, string key) => Lookup(_fileModel, table, key);

    public string TextOf(string table, string key, string fallback) =>
        Get(table, key) is string text ? text : fallback;

    public bool FileContains(string table, string key) => _file.Contains(table, key);

    public bool IsDirty(string path)
    {
        if (_dirty.Contains(path))
        {
            return true;
        }

        foreach (var entry in _dirty)
        {
            if (entry.Length > path.Length && entry.StartsWith(path, StringComparison.Ordinal) && entry[path.Length] is '.' or '#')
            {
                return true;
            }
        }

        return false;
    }

    public string? ErrorOf(string path) => _errors.TryGetValue(path, out var why) ? why : null;

    public void SetError(string path, string? why)
    {
        if (why is null)
        {
            _errors.Remove(path);
        }
        else
        {
            _errors[path] = why;
        }
    }

    public void ClearErrors() => _errors.Clear();

    public string? HintOf(string path) => _hints.TryGetValue(path, out var hint) ? hint : null;

    public void SetHint(string path, string? hint)
    {
        _hints.Clear();
        if (hint is not null)
        {
            _hints[path] = hint;
        }
    }

    public bool HasErrors => _errors.Count > 0;

    public void Set(SettingKey key, TomlValue? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (value is not null && key.Default is { } fallback && value.Text == fallback && !_file.Contains(key.Table, key.Key))
        {
            value = null;
        }

        Set(key.Table, key.Key, value);
    }

    public void Set(string table, string key, TomlValue? value)
    {
        if (value is null)
        {
            _draft.Remove(table, key);
        }
        else
        {
            _draft.Set(table, key, value);
        }

        Touch(table.Length == 0 ? key : table + "." + key);
    }

    public void Rename(string table, string from, string to)
    {
        if (string.Equals(from, to, StringComparison.Ordinal) || _draft.RawValue(table, from) is not { } raw)
        {
            return;
        }

        _draft.Remove(table, from);
        _draft.Set(table, to, TomlValue.Parse(raw));
        Touch(table + "." + to);
    }

    public void Reset(IEnumerable<SettingKey> keys, Action<TomlDocument>? also, string path)
    {
        ArgumentNullException.ThrowIfNull(keys);
        also?.Invoke(_draft);
        foreach (var key in keys)
        {
            if (key.Default is { } fallback && _file.Contains(key.Table, key.Key))
            {
                _draft.Set(key.Table, key.Key, TomlValue.Parse(fallback));
            }
            else
            {
                _draft.Remove(key.Table, key.Key);
            }
        }

        Touch(path);
    }

    public void Edit(Action<TomlDocument> edit, string path)
    {
        ArgumentNullException.ThrowIfNull(edit);
        edit(_draft);
        Touch(path);
    }

    public TomlTableArray? Array(string name)
    {
        var segments = TomlDocument.Segments(name);
        if (segments.Count == 0)
        {
            return null;
        }

        var parent = Navigate(_draftModel, segments.Take(segments.Count - 1).ToArray());
        return parent is not null && parent.TryGetValue(segments[^1], out var value) ? value as TomlTableArray : null;
    }

    public TomlTable? Table(string table) => Navigate(_draftModel, TomlDocument.Segments(table));

    public void Revert(string status)
    {
        if (FilePath is { } path && File.Exists(path))
        {
            try
            {
                _file = TomlDocument.Load(path);
                Refusal = null;
            }
            catch (Exception error) when (error is FormatException or IOException or UnauthorizedAccessException)
            {
                Refusal = $"{path} does not read: {error.Message}";
                _file = TomlDocument.Parse(string.Empty);
            }
        }

        _draft = TomlDocument.Parse(_file.ToString());
        _fileModel = Toml.ToModel(_file.ToString());
        Stamp();
        Conflict = false;
        _errors.Clear();
        _hints.Clear();
        Status = status;
        Touch(null);
    }

    public string? Save(bool overwrite)
    {
        if (SaveBlocked is { } blocked)
        {
            return blocked;
        }

        var path = FilePath!;
        if (!overwrite && ChangedOnDisk())
        {
            Conflict = true;
            Status = "the file changed on disk";
            Changed?.Invoke();
            return Status;
        }

        try
        {
            _draft.Save(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Status = $"save failed: {error.Message}";
            Changed?.Invoke();
            return Status;
        }

        _file = TomlDocument.Parse(_draft.ToString());
        _fileModel = Toml.ToModel(_file.ToString());
        Stamp();
        Conflict = false;
        Status = "saved";
        Touch(null);
        return null;
    }

    public bool ChangedOnDisk()
    {
        if (FilePath is not { } path)
        {
            return false;
        }

        var info = new FileInfo(path);
        return info.Exists ? info.Length != _fileLength || info.LastWriteTimeUtc != _fileWrite : _fileLength != 0;
    }

    private void Stamp()
    {
        if (FilePath is { } path && new FileInfo(path) is { Exists: true } info)
        {
            _fileLength = info.Length;
            _fileWrite = info.LastWriteTimeUtc;
        }
        else
        {
            _fileLength = 0;
            _fileWrite = default;
        }
    }

    private void Touch(string? path)
    {
        _draftModel = Toml.ToModel(_draft.ToString());
        _dirty.Clear();
        Diff(_fileModel, _draftModel, string.Empty);
        if (path is not null)
        {
            _errors.Remove(path);
            LastPath = path;
        }

        Version++;
        Changed?.Invoke();
    }

    private void Diff(TomlTable before, TomlTable after, string prefix)
    {
        foreach (var (key, value) in after)
        {
            var path = prefix + key;
            if (!before.TryGetValue(key, out var old))
            {
                MarkAll(path, value);
                continue;
            }

            Compare(path, old, value);
        }

        foreach (var (key, value) in before)
        {
            if (!after.ContainsKey(key))
            {
                MarkAll(prefix + key, value);
            }
        }
    }

    private void Compare(string path, object old, object value)
    {
        switch (old, value)
        {
            case (TomlTable a, TomlTable b):
                Diff(a, b, path + ".");
                break;
            case (TomlTableArray a, TomlTableArray b):
                for (var i = 0; i < Math.Max(a.Count, b.Count); i++)
                {
                    var item = $"{path}#{i}";
                    if (i >= a.Count)
                    {
                        MarkAll(item, b[i]);
                    }
                    else if (i >= b.Count)
                    {
                        MarkAll(item, a[i]);
                    }
                    else
                    {
                        Diff(a[i], b[i], item + ".");
                    }
                }

                break;
            default:
                if (!Same(old, value))
                {
                    _dirty.Add(path);
                }

                break;
        }
    }

    private void MarkAll(string path, object value)
    {
        switch (value)
        {
            case TomlTable table when table.Count > 0:
                foreach (var (key, inner) in table)
                {
                    MarkAll(path + "." + key, inner);
                }

                break;
            case TomlTableArray array when array.Count > 0:
                for (var i = 0; i < array.Count; i++)
                {
                    MarkAll($"{path}#{i}", array[i]);
                }

                break;
            default:
                _dirty.Add(path);
                break;
        }
    }

    public static bool Same(object? a, object? b)
    {
        switch (a, b)
        {
            case (null, null):
                return true;
            case (long x, long y):
                return x == y;
            case (long or double, long or double):
                return Convert.ToDouble(a, CultureInfo.InvariantCulture) == Convert.ToDouble(b, CultureInfo.InvariantCulture);
            case (TomlArray x, TomlArray y):
                if (x.Count != y.Count)
                {
                    return false;
                }

                for (var i = 0; i < x.Count; i++)
                {
                    if (!Same(x[i], y[i]))
                    {
                        return false;
                    }
                }

                return true;
            case (TomlTable x, TomlTable y):
                if (x.Count != y.Count)
                {
                    return false;
                }

                foreach (var (key, value) in x)
                {
                    if (!y.TryGetValue(key, out var other) || !Same(value, other))
                    {
                        return false;
                    }
                }

                return true;
            default:
                return Equals(a, b);
        }
    }

    private static object? Lookup(TomlTable model, string table, string key) =>
        Navigate(model, TomlDocument.Segments(table)) is { } scope && scope.TryGetValue(key, out var value) ? value : null;

    private static TomlTable? Navigate(TomlTable model, IReadOnlyList<string> segments)
    {
        var scope = model;
        foreach (var segment in segments)
        {
            if (!scope.TryGetValue(segment, out var next) || next is not TomlTable table)
            {
                return null;
            }

            scope = table;
        }

        return scope;
    }
}
