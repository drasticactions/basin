using Basin.Diagnostics;
using Tomlyn.Model;

namespace Basin.Config;

public sealed class TomlReader
{
    private readonly TomlTable _table;
    private readonly HashSet<string> _read = new(StringComparer.Ordinal);
    private readonly List<TomlReader> _children = [];
    private readonly string _prefix;

    public TomlReader(TomlTable table, BasinLogger log)
        : this(table, string.Empty, log)
    {
    }

    private TomlReader(TomlTable table, string prefix, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(table);
        _table = table;
        _prefix = prefix;
        Log = log;
    }

    public BasinLogger Log { get; }

    public TomlTable Table => _table;

    public TomlReader? Section(string key)
    {
        if (Take(key) is not TomlTable child)
        {
            return null;
        }

        var reader = new TomlReader(child, _prefix + key + ".", Log);
        _children.Add(reader);
        return reader;
    }

    public IReadOnlyList<TomlReader> Sections(string key)
    {
        if (Take(key) is not TomlTableArray rows)
        {
            return [];
        }

        var readers = new List<TomlReader>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var reader = new TomlReader(rows[i], $"{_prefix}{key}[{i}].", Log);
            _children.Add(reader);
            readers.Add(reader);
        }

        return readers;
    }

    public TomlTable? Free(string key) => Take(key) as TomlTable;

    public TomlTableArray? FreeArray(string key) => Take(key) as TomlTableArray;

    public bool Flag(string key, bool fallback)
    {
        var value = Take(key);
        if (value is null)
        {
            return fallback;
        }

        if (value is bool flag)
        {
            return flag;
        }

        Warn(key, "expected true or false");
        return fallback;
    }

    public string? Text(string key)
    {
        var value = Take(key);
        if (value is null)
        {
            return null;
        }

        if (value is string text)
        {
            return text.Length > 0 ? text : null;
        }

        Warn(key, "expected a string");
        return null;
    }

    public string Choice(string key, string fallback, params string[] allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        var value = Take(key);
        if (value is null)
        {
            return fallback;
        }

        if (value is string text && Array.IndexOf(allowed, text) >= 0)
        {
            return text;
        }

        Warn(key, $"expected one of {string.Join(", ", allowed)}, keeping {fallback}");
        return fallback;
    }

    public int Number(string key, int fallback)
    {
        var value = Take(key);
        if (value is null)
        {
            return fallback;
        }

        if (value is long number)
        {
            return (int)number;
        }

        Warn(key, "expected a whole number");
        return fallback;
    }

    public double Number(string key, double fallback)
    {
        var value = Take(key);
        return value switch
        {
            null => fallback,
            double number => number,
            long integer => integer,
            _ => WarnAnd(key, "expected a number", fallback),
        };
    }

    public double[]? Numbers(string key)
    {
        var value = Take(key);
        if (value is null)
        {
            return null;
        }

        if (value is double single)
        {
            return [single];
        }

        if (value is long integer)
        {
            return [integer];
        }

        if (value is TomlArray array)
        {
            var parsed = new double[array.Count];
            for (var i = 0; i < array.Count; i++)
            {
                switch (array[i])
                {
                    case double number:
                        parsed[i] = number;
                        break;
                    case long whole:
                        parsed[i] = whole;
                        break;
                    default:
                        Warn(key, "expected an array of numbers");
                        return null;
                }
            }

            return parsed;
        }

        Warn(key, "expected a number or an array of numbers");
        return null;
    }

    public string[]? Words(string key)
    {
        var value = Take(key);
        var parts = value switch
        {
            string text => text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            TomlArray array => [.. array.OfType<string>().Select(static part => part.Trim()).Where(static part => part.Length > 0)],
            null => Array.Empty<string>(),
            _ => Array.Empty<string>(),
        };
        return parts.Length > 0 ? parts : null;
    }

    public void ReportUnknown()
    {
        foreach (var (key, _) in _table)
        {
            if (!_read.Contains(key))
            {
                Log.Warn($"unknown key '{_prefix}{key}', ignored");
            }
        }

        foreach (var child in _children)
        {
            child.ReportUnknown();
        }
    }

    private object? Take(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        _read.Add(key);
        return _table.TryGetValue(key, out var value) ? value : null;
    }

    private void Warn(string key, string wanted) => Log.Warn($"{_prefix}{key}: {wanted}");

    private double WarnAnd(string key, string wanted, double fallback)
    {
        Warn(key, wanted);
        return fallback;
    }
}
