using Basin.Diagnostics;
using Tomlyn.Model;

namespace Basin.Config;

/// <summary>
/// Reads typed values from a TOML table, warns about values of the wrong type and records each key it reads.
/// <see cref="ReportUnknown"/> then warns about every key that nothing read.
/// </summary>
public sealed class TomlReader
{
    private readonly TomlTable _table;
    private readonly HashSet<string> _read = new(StringComparer.Ordinal);
    private readonly List<TomlReader> _children = [];
    private readonly Dictionary<string, TomlReader> _sections = new(StringComparer.Ordinal);
    private readonly string _prefix;

    /// <summary>
    /// Initializes a new instance of the <see cref="TomlReader"/> class for a root table.
    /// </summary>
    /// <param name="table">The table to read.</param>
    /// <param name="log">The logger for warnings.</param>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Gets the logger that receives the warnings of this reader and of its sections.
    /// </summary>
    public BasinLogger Log { get; }

    /// <summary>
    /// Gets the table that this reader reads. Access through it does not mark keys as read.
    /// </summary>
    public TomlTable Table => _table;

    /// <summary>
    /// Gets a reader for a sub-table and marks the key as read. A second call for the same table gives the same reader.
    /// </summary>
    /// <param name="key">The key of the sub-table.</param>
    /// <returns>The reader, or <see langword="null"/> when the key is missing or is not a table.</returns>
    public TomlReader? Section(string key)
    {
        if (Take(key) is not TomlTable child)
        {
            return null;
        }

        if (_sections.TryGetValue(key, out var known) && ReferenceEquals(known._table, child))
        {
            return known;
        }

        var reader = new TomlReader(child, _prefix + key + ".", Log);
        _children.Add(reader);
        _sections[key] = reader;
        return reader;
    }

    /// <summary>
    /// Gets a reader for each row of an array of tables and marks the key as read. Each call makes new readers.
    /// </summary>
    /// <param name="key">The key of the array of tables.</param>
    /// <returns>The readers in row order, or an empty list when the key is missing or is not an array of tables.</returns>
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

    /// <summary>
    /// Gets a sub-table without a reader, so its keys are not checked, and marks the key as read.
    /// </summary>
    /// <param name="key">The key of the sub-table.</param>
    /// <returns>The table, or <see langword="null"/> when the key is missing or is not a table.</returns>
    public TomlTable? Free(string key) => Take(key) as TomlTable;

    /// <summary>
    /// Gets the value of a key with no type check and marks the key as read.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The Tomlyn model value, or <see langword="null"/> when the key is missing.</returns>
    public object? Raw(string key) => Take(key);

    /// <summary>
    /// Gives the dotted path of a key from the root, for warnings, for example <c>input.device[0].name</c>.
    /// </summary>
    /// <param name="key">The key in this table.</param>
    /// <returns>The full key path.</returns>
    public string Where(string key) => _prefix + key;

    /// <summary>
    /// Gets an array of tables without readers, so its keys are not checked, and marks the key as read.
    /// </summary>
    /// <param name="key">The key of the array.</param>
    /// <returns>The array, or <see langword="null"/> when the key is missing or is not an array of tables.</returns>
    public TomlTableArray? FreeArray(string key) => Take(key) as TomlTableArray;

    /// <summary>
    /// Reads a boolean key and warns when the value is not a boolean.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="fallback">The value when the key is missing or invalid.</param>
    /// <returns>The value of the key, or <paramref name="fallback"/>.</returns>
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

    /// <summary>
    /// Reads a string key and warns when the value is not a string.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The string, or <see langword="null"/> when the key is missing, empty or invalid.</returns>
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

    /// <summary>
    /// Reads a string key that must be one of a set of words, and warns when it is not.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="fallback">The value when the key is missing or invalid.</param>
    /// <param name="allowed">The allowed words, compared exactly.</param>
    /// <returns>The word, or <paramref name="fallback"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="allowed"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Reads an integer key and warns when the value is not an integer. A value outside the <see cref="int"/> range is truncated.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="fallback">The value when the key is missing or invalid.</param>
    /// <returns>The value of the key, or <paramref name="fallback"/>.</returns>
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

    /// <summary>
    /// Reads a float or integer key and warns when the value is not a number.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="fallback">The value when the key is missing or invalid.</param>
    /// <returns>The value of the key, or <paramref name="fallback"/>.</returns>
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

    /// <summary>
    /// Reads a number or an array of numbers, and warns when the value is something else.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The numbers, or <see langword="null"/> when the key is missing or invalid.</returns>
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

    /// <summary>
    /// Reads a command as a string split on spaces or as an array of strings. Empty words are dropped. Other types give no warning.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The words, or <see langword="null"/> when there are none.</returns>
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

    /// <summary>
    /// Warns about each key of this table and of each child section that no read call touched.
    /// </summary>
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
