using System.Globalization;
using Basin.Config;
using Basin.UI.Paper;
using Tomlyn.Model;

namespace TinyComp;

internal sealed class SettingSlot : ISettingValue<bool>, ISettingValue<string>, ISettingValue<double>
{
    private readonly SettingsDraft _draft;
    private readonly SettingsContext _context;
    private readonly string _table;
    private readonly string _key;
    private readonly SettingKind _kind;
    private readonly string? _fallback;
    private readonly int _rule;

    public SettingSlot(
        SettingsDraft draft, SettingsContext context, string table, string key, SettingKind kind, string? fallback, int rule = -1)
    {
        _draft = draft;
        _context = context;
        _table = table;
        _key = key;
        _kind = kind;
        _fallback = fallback;
        _rule = rule;
        Path = rule < 0 ? (table.Length == 0 ? key : table + "." + key) : $"{table}#{rule}.{key}";
    }

    public SettingKey? Key { get; init; }

    public string? Inherit { get; init; }

    public bool OnOff { get; init; }

    public string Path { get; }

    public bool IsDirty => _draft.IsDirty(Path);

    public string? Badge
    {
        get
        {
            if (Key is not { } key)
            {
                return null;
            }

            if (key.FlagKey is { } flag && _context.FromFlags.Contains(flag))
            {
                return $"overridden by {key.Flag} this run";
            }

            return key.Restart ? "applies after restart" : null;
        }
    }

    public string? Error => _draft.ErrorOf(Path);

    public string? Hint => _draft.HintOf(Path);

    bool ISettingValue<bool>.Value
    {
        get => Read() is bool flag ? flag : string.Equals(_fallback, "true", StringComparison.Ordinal);
        set => Write(TomlValue.From(value));
    }

    string ISettingValue<string>.Value
    {
        get
        {
            if (_kind == SettingKind.Literal)
            {
                return Raw() ?? string.Empty;
            }

            return Read() switch
            {
                string text => text,
                bool flag when OnOff => flag ? "on" : "off",
                bool flag => flag ? "true" : "false",
                long number => number.ToString(CultureInfo.InvariantCulture),
                double real => real.ToString("0.###", CultureInfo.InvariantCulture),
                null => Inherit ?? Unquote(_fallback) ?? string.Empty,
                _ => Raw() ?? string.Empty,
            };
        }

        set
        {
            if (_kind == SettingKind.Literal)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    Write(null);
                }
                else if (TomlValue.TryParse(value, out var parsed))
                {
                    Write(parsed);
                }
                else
                {
                    _draft.SetError(Path, "Put text in quotes, like \"warp\".");
                }

                return;
            }

            if (Inherit is not null && string.Equals(value, Inherit, StringComparison.Ordinal))
            {
                Write(null);
            }
            else if (OnOff && value is "on" or "off")
            {
                Write(TomlValue.From(value == "on"));
            }
            else
            {
                Write(TomlValue.From(value));
            }
        }
    }

    double ISettingValue<double>.Value
    {
        get => Read() switch
        {
            long number => number,
            double real => real,
            _ => double.TryParse(_fallback, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0,
        };

        set => Write(_kind == SettingKind.Integer || (Math.Abs(value - Math.Round(value)) < 1e-9 && (Read() is long || Key?.Step >= 1))
            ? TomlValue.From((long)Math.Round(value))
            : TomlValue.From(value));
    }

    public object? Read()
    {
        if (_rule < 0)
        {
            return _draft.Get(_table, _key);
        }

        return _draft.Array(_table) is { } rows && _rule < rows.Count && rows[_rule].TryGetValue(_key, out var value) ? value : null;
    }

    public string? Raw() => _rule < 0
        ? _draft.Document.RawValue(_table, _key)
        : _rule < _draft.Document.TableCount(_table) ? _draft.Document.RawValueInTable(_table, _rule, _key) : null;

    public void Write(TomlValue? value)
    {
        if (_rule >= 0)
        {
            var index = _rule;
            var table = _table;
            var key = _key;
            _draft.Edit(
                document =>
                {
                    if (value is null)
                    {
                        document.RemoveInTable(table, index, key);
                    }
                    else
                    {
                        document.SetInTable(table, index, key, value);
                    }
                },
                Path);
            return;
        }

        if (Key is { } catalog)
        {
            _draft.Set(catalog, value);
        }
        else
        {
            _draft.Set(_table, _key, value);
        }
    }

    public static string? Unquote(string? literal)
    {
        if (literal is null)
        {
            return null;
        }

        try
        {
            return Tomlyn.Toml.ToModel("v = " + literal)["v"] switch
            {
                string text => text,
                TomlArray => string.Empty,
                var other => Convert.ToString(other, CultureInfo.InvariantCulture),
            };
        }
        catch (Tomlyn.TomlException)
        {
            return literal;
        }
    }
}
