using System.Globalization;
using Basin.Capabilities;
using Basin.Diagnostics;
using Tomlyn.Model;

namespace Basin.Config;

/// <summary>
/// Reads input device rules from the <c>[input]</c> table and writes single settings back to a <see cref="TomlDocument"/>.
/// </summary>
public static class InputConfig
{
    /// <summary>
    /// The name of the table that holds the catch-all settings and the per-type tables.
    /// </summary>
    public const string Table = "input";

    /// <summary>
    /// The name of the array of tables that holds the per-device rules.
    /// </summary>
    public const string DeviceTable = "input.device";

    private static readonly string[] MatchKeys = ["name", "sysname", "vendor", "product", "output"];

    /// <summary>
    /// Reads the catch-all keys, the <c>[input.TYPE]</c> tables and the <c>[[input.device]]</c> rows.
    /// The catch-all rule comes first, then the other rules in file order. A rule with no valid setting is dropped.
    /// </summary>
    /// <param name="input">The reader of the <c>[input]</c> table.</param>
    /// <param name="log">The logger for warnings about invalid values.</param>
    /// <returns>The rules. <see cref="InputRule.Line"/> holds the position of each rule in file order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<InputRule> Read(TomlReader input, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(input);
        var rules = new List<InputRule>();
        var catchAll = new List<(InputSetting, InputSettingValue)>();
        var keys = new List<string>(input.Table.Keys);
        foreach (var key in keys)
        {
            var value = input.Table[key];
            if (value is TomlTable)
            {
                if (key == "device" || !InputSettingNames.TryParseType(key, out var type))
                {
                    continue;
                }

                var section = input.Section(key)!;
                var match = type == InputDeviceType.Keyboard ? new InputMatch(AnyKeyboard: true) : new InputMatch(Type: type);
                Add(rules, match, ReadSettings(section, log, skip: null));
                continue;
            }

            if (value is TomlTableArray)
            {
                if (key != "device")
                {
                    continue;
                }

                foreach (var row in input.Sections("device"))
                {
                    if (ReadMatch(row, log) is { } match)
                    {
                        Add(rules, match, ReadSettings(row, log, skip: MatchKeys));
                    }
                }

                continue;
            }

            if (InputSettingNames.TryParseKey(key, out var setting) && TryRead(input, key, setting, log, out var parsed))
            {
                catchAll.Add((setting, parsed));
            }
        }

        if (catchAll.Count > 0)
        {
            rules.Insert(0, new InputRule { Match = default, Settings = catchAll });
        }

        return rules;
    }

    /// <summary>
    /// Gives the TOML table that a rule comes from, for log lines, for example <c>[input.touchpad]</c> or <c>[[input.device]] name=...</c>.
    /// </summary>
    /// <param name="rule">The rule to describe.</param>
    /// <returns>The table header and, for a device rule, its match keys.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> is <see langword="null"/>.</exception>
    public static string Describe(InputRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var match = rule.Match;
        if (match.IsCatchAll)
        {
            return "[input]";
        }

        if (!match.NamesDevice)
        {
            return match.AnyKeyboard ? "[input.keyboard]" : $"[input.{InputSettingNames.TypeName(match.Type!.Value)}]";
        }

        var parts = new List<string>();
        if (match.NameGlob is { } name)
        {
            parts.Add($"name={name}");
        }

        if (match.Sysname is { } sysname)
        {
            parts.Add($"sysname={sysname}");
        }

        if (match.Vendor is { } vendor)
        {
            parts.Add($"vendor=0x{vendor:x4}");
        }

        if (match.Product is { } product)
        {
            parts.Add($"product=0x{product:x4}");
        }

        if (match.Output is { } output)
        {
            parts.Add($"output={output}");
        }

        return $"[[input.device]] {string.Join(' ', parts)}";
    }

    /// <summary>
    /// Writes one setting into the table that the match names. A device match with no row yet gets a new <c>[[input.device]]</c> row with its match keys.
    /// </summary>
    /// <param name="doc">The document to change.</param>
    /// <param name="match">The match that selects the table or the device row.</param>
    /// <param name="setting">The setting to write.</param>
    /// <param name="value">The value to write.</param>
    /// <exception cref="ArgumentNullException"><paramref name="doc"/> is <see langword="null"/>.</exception>
    public static void Set(TomlDocument doc, InputMatch match, InputSetting setting, in InputSettingValue value)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var literal = Literal(setting, value);
        var key = InputSettingNames.Key(setting);
        if (!match.NamesDevice)
        {
            doc.Set(TableFor(match), key, literal);
            return;
        }

        var index = FindDevice(doc, match);
        if (index < 0)
        {
            index = doc.AppendTable(DeviceTable);
            WriteMatch(doc, index, match);
        }

        doc.SetInTable(DeviceTable, index, key, literal);
    }

    /// <summary>
    /// Removes one setting from the table that the match names.
    /// </summary>
    /// <param name="doc">The document to change.</param>
    /// <param name="match">The match that selects the table or the device row.</param>
    /// <param name="setting">The setting to remove.</param>
    /// <returns><see langword="true"/> when the key was present and is removed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="doc"/> is <see langword="null"/>.</exception>
    public static bool Remove(TomlDocument doc, InputMatch match, InputSetting setting)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var key = InputSettingNames.Key(setting);
        if (!match.NamesDevice)
        {
            return doc.Remove(TableFor(match), key);
        }

        var index = FindDevice(doc, match);
        return index >= 0 && doc.RemoveInTable(DeviceTable, index, key);
    }

    /// <summary>
    /// Converts a setting value to the TOML literal for its kind: a boolean, an integer, a float, an array of floats or a string.
    /// </summary>
    /// <param name="setting">The setting that sets the kind.</param>
    /// <param name="value">The value to convert.</param>
    /// <returns>The TOML literal.</returns>
    public static TomlValue Literal(InputSetting setting, in InputSettingValue value)
    {
        switch (InputSettingNames.KindOf(setting))
        {
            case InputSettingKind.Flag:
                return TomlValue.From(value.IsOn);
            case InputSettingKind.Integer:
                return TomlValue.From((long)value.Value);
            case InputSettingKind.Number:
                return TomlValue.From(value.Count > 0 ? value[0] : 0);
            case InputSettingKind.Matrix:
                var items = new TomlValue[value.Count];
                for (var i = 0; i < items.Length; i++)
                {
                    items[i] = TomlValue.From(value[i]);
                }

                return TomlValue.Array(items);
            default:
                return TomlValue.From(InputSettingNames.Format(setting, value));
        }
    }

    private static string TableFor(InputMatch match)
    {
        if (match.IsCatchAll)
        {
            return Table;
        }

        return match.AnyKeyboard || match.Type == InputDeviceType.Keyboard
            ? "input.keyboard"
            : $"input.{InputSettingNames.TypeName(match.Type!.Value)}";
    }

    /// <summary>
    /// Finds the <c>[[input.device]]</c> row whose match keys are exactly the keys of the match. A key that the match does not set must be absent from the row.
    /// </summary>
    /// <param name="doc">The document to search.</param>
    /// <param name="match">The device match.</param>
    /// <returns>The zero-based row index, or -1 when no row has the same match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="doc"/> is <see langword="null"/>.</exception>
    public static int FindDevice(TomlDocument doc, InputMatch match)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var count = doc.TableCount(DeviceTable);
        for (var i = 0; i < count; i++)
        {
            if (SameText(doc.RawValueInTable(DeviceTable, i, "name"), match.NameGlob) &&
                SameText(doc.RawValueInTable(DeviceTable, i, "sysname"), match.Sysname) &&
                SameText(doc.RawValueInTable(DeviceTable, i, "output"), match.Output) &&
                SameNumber(doc.RawValueInTable(DeviceTable, i, "vendor"), match.Vendor) &&
                SameNumber(doc.RawValueInTable(DeviceTable, i, "product"), match.Product))
            {
                return i;
            }
        }

        return -1;
    }

    private static void WriteMatch(TomlDocument doc, int index, InputMatch match)
    {
        if (match.NameGlob is { } name)
        {
            doc.SetInTable(DeviceTable, index, "name", TomlValue.From(name));
        }

        if (match.Sysname is { } sysname)
        {
            doc.SetInTable(DeviceTable, index, "sysname", TomlValue.From(sysname));
        }

        if (match.Vendor is { } vendor)
        {
            doc.SetInTable(DeviceTable, index, "vendor", TomlValue.Parse($"0x{vendor:x4}"));
        }

        if (match.Product is { } product)
        {
            doc.SetInTable(DeviceTable, index, "product", TomlValue.Parse($"0x{product:x4}"));
        }

        if (match.Output is { } output)
        {
            doc.SetInTable(DeviceTable, index, "output", TomlValue.From(output));
        }
    }

    private static bool SameText(string? raw, string? wanted) =>
        wanted is null ? raw is null : raw is not null && raw == TomlValue.From(wanted).Text;

    private static bool SameNumber(string? raw, uint? wanted)
    {
        if (wanted is null)
        {
            return raw is null;
        }

        return raw is not null && ParseLiteralNumber(raw.Trim()) == wanted;
    }

    private static uint? ParseLiteralNumber(string literal)
    {
        var text = literal.Replace("_", string.Empty, StringComparison.Ordinal);
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex : null;
        }

        return uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    private static void Add(List<InputRule> rules, InputMatch match, List<(InputSetting, InputSettingValue)> settings)
    {
        if (settings.Count > 0)
        {
            rules.Add(new InputRule { Match = match, Settings = settings, Line = rules.Count });
        }
    }

    private static InputMatch? ReadMatch(TomlReader row, BasinLogger log)
    {
        var name = row.Text("name");
        var sysname = row.Text("sysname");
        var output = row.Text("output");
        var vendor = ReadId(row, "vendor", log);
        var product = ReadId(row, "product", log);
        var match = new InputMatch(NameGlob: name, Sysname: sysname, Vendor: vendor, Product: product, Output: output);
        if (!match.NamesDevice)
        {
            log.Warn($"{row.Where("name")}: an [[input.device]] entry needs name, sysname, vendor, product or output, ignored");
            return null;
        }

        return match;
    }

    private static uint? ReadId(TomlReader row, string key, BasinLogger log)
    {
        switch (row.Raw(key))
        {
            case null:
                return null;
            case long number when number is >= 0 and <= uint.MaxValue:
                return (uint)number;
            case string text when ParseLiteralNumber(text.Trim()) is { } parsed:
                return parsed;
            default:
                log.Warn($"{row.Where(key)}: expected a number such as 0x046d, ignored");
                return null;
        }
    }

    private static List<(InputSetting, InputSettingValue)> ReadSettings(TomlReader section, BasinLogger log, string[]? skip)
    {
        var settings = new List<(InputSetting, InputSettingValue)>();
        var keys = new List<string>(section.Table.Keys);
        foreach (var key in keys)
        {
            if (skip is not null && Array.IndexOf(skip, key) >= 0)
            {
                continue;
            }

            if (InputSettingNames.TryParseKey(key, out var setting) && TryRead(section, key, setting, log, out var value))
            {
                settings.Add((setting, value));
            }
        }

        return settings;
    }

    private static bool TryRead(TomlReader section, string key, InputSetting setting, BasinLogger log, out InputSettingValue value)
    {
        var raw = section.Raw(key);
        var text = raw switch
        {
            bool flag => flag ? "true" : "false",
            long integer => integer.ToString(CultureInfo.InvariantCulture),
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            string word => word,
            TomlArray array => JoinNumbers(array),
            _ => null,
        };
        if (text is not null && InputSettingNames.TryParse(setting, text, out value))
        {
            return true;
        }

        var words = InputSettingNames.Words(setting);
        var wanted = words.Length > 0 ? $"one of {string.Join(", ", words.ToArray())}" : InputSettingNames.KindOf(setting) switch
        {
            InputSettingKind.Integer => "a whole number",
            InputSettingKind.Number => "a number",
            InputSettingKind.Matrix => "six numbers",
            _ => "a string",
        };
        log.Warn($"{section.Where(key)}: expected {wanted}, ignored");
        value = default;
        return false;
    }

    private static string? JoinNumbers(TomlArray array)
    {
        var parts = new string[array.Count];
        for (var i = 0; i < array.Count; i++)
        {
            parts[i] = array[i] switch
            {
                double number => number.ToString("R", CultureInfo.InvariantCulture),
                long integer => integer.ToString(CultureInfo.InvariantCulture),
                _ => string.Empty,
            };
            if (parts[i].Length == 0)
            {
                return null;
            }
        }

        return string.Join(' ', parts);
    }
}
