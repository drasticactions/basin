using System.Globalization;
using Basin.Capabilities;
using Basin.Config;
using Basin.UI.Paper;
using Tomlyn.Model;

namespace TinyComp;

internal sealed class InputSlot(SettingsDraft draft, InputMatch match, InputSetting setting, string? badge, string? current = null)
    : ISettingValue<string>
{
    public const string Default = "(default)";

    private readonly string _key = InputSettingNames.Key(setting);

    public bool IsChoice => InputSettingNames.KindOf(setting) is InputSettingKind.Flag or InputSettingKind.Choice;

    public string Path
    {
        get
        {
            if (!match.NamesDevice)
            {
                return Table + "." + _key;
            }

            var index = InputConfig.FindDevice(draft.Document, match);
            return index >= 0 ? $"{InputConfig.DeviceTable}#{index}.{_key}" : $"{InputConfig.DeviceTable}.{_key}";
        }
    }

    public string Value
    {
        get => Text(Read()) ?? (IsChoice ? Default : string.Empty);
        set
        {
            var path = Path;
            if (string.IsNullOrWhiteSpace(value) || value == Default)
            {
                draft.Edit(document => InputConfig.Remove(document, match, setting), path);
                return;
            }

            if (!InputSettingNames.TryParse(setting, value, out var parsed))
            {
                var words = InputSettingNames.Words(setting);
                draft.SetError(path, words.Length > 0 ? $"Choose {(words.Length == 1 ? words[0] : $"{string.Join(", ", words[..^1].ToArray())} or {words[^1]}")}." : $"{_key} can't take this value.");
                return;
            }

            draft.Edit(document => InputConfig.Set(document, match, setting, parsed), path);
        }
    }

    public bool IsDirty => draft.IsDirty(Path);

    public string? Badge => badge;

    public string? Error => draft.ErrorOf(Path);

    public string? Hint => current is { Length: > 0 } now ? $"now {now}" : null;

    private string Table => match.IsCatchAll
        ? InputConfig.Table
        : match.AnyKeyboard || match.Type == InputDeviceType.Keyboard
            ? "input.keyboard"
            : "input." + InputSettingNames.TypeName(match.Type!.Value);

    private object? Read()
    {
        if (!match.NamesDevice)
        {
            return draft.Get(Table, _key);
        }

        var index = InputConfig.FindDevice(draft.Document, match);
        return index >= 0 && draft.Array(InputConfig.DeviceTable) is { } rows && index < rows.Count &&
            rows[index].TryGetValue(_key, out var value)
            ? value
            : null;
    }

    private static string? Text(object? value) => value switch
    {
        null => null,
        bool flag => flag ? "true" : "false",
        long number => number.ToString(CultureInfo.InvariantCulture),
        double real => real.ToString("R", CultureInfo.InvariantCulture),
        string text => text,
        TomlArray array => string.Join(' ', array.Select(item => Convert.ToString(item, CultureInfo.InvariantCulture))),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
