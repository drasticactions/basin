using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Xkb;

namespace Basin.Seat;

public static partial class KeymapEdit
{
    [GeneratedRegex(@"<(?<name>[A-Za-z0-9_+\-]+)>\s*=\s*(?<code>\d+)\s*;")]
    private static partial Regex KeycodeLine();

    public static string? WithKeysyms(string keymap, ReadOnlySpan<(uint Keycode, uint Keysym)> keys)
    {
        ArgumentNullException.ThrowIfNull(keymap);
        var keycodesAt = keymap.IndexOf("xkb_keycodes", StringComparison.Ordinal);
        var symbolsAt = keymap.IndexOf("xkb_symbols", StringComparison.Ordinal);
        if (keycodesAt < 0 || symbolsAt < 0)
        {
            return null;
        }

        var keycodesEnd = keymap.IndexOf("\n};", keycodesAt, StringComparison.Ordinal);
        var symbolsEnd = keymap.IndexOf("\n};", symbolsAt, StringComparison.Ordinal);
        if (keycodesEnd < 0 || symbolsEnd < 0)
        {
            return null;
        }

        var names = new Dictionary<uint, string>();
        foreach (Match match in KeycodeLine().Matches(keymap[keycodesAt..keycodesEnd]))
        {
            var code = uint.Parse(match.Groups["code"].ValueSpan, CultureInfo.InvariantCulture);
            names.TryAdd(code, match.Groups["name"].Value);
        }

        var addedKeycodes = new StringBuilder();
        var addedSymbols = new StringBuilder();
        foreach (var (keycode, keysym) in keys)
        {
            var name = new XkbKeysym(keysym).ToString();
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var xkbCode = keycode + 8;
            if (!names.TryGetValue(xkbCode, out var keyName))
            {
                keyName = $"BX{keycode:X2}";
                addedKeycodes.Append(CultureInfo.InvariantCulture, $"\n\t<{keyName}> = {xkbCode};");
            }

            addedSymbols.Append(CultureInfo.InvariantCulture, $"\n\tkey <{keyName}> {{ [ {name} ] }};");
        }

        var text = new StringBuilder(keymap.Length + addedKeycodes.Length + addedSymbols.Length);
        text.Append(keymap, 0, keycodesEnd);
        text.Append(addedKeycodes);
        text.Append(keymap, keycodesEnd, symbolsEnd - keycodesEnd);
        text.Append(addedSymbols);
        text.Append(keymap, symbolsEnd, keymap.Length - symbolsEnd);
        return text.ToString();
    }
}
