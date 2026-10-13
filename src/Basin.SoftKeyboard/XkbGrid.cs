using System.Globalization;
using System.Text;
using Xkb;

namespace Basin.SoftKeyboard;

public static class XkbGrid
{
    private static readonly uint[] NumberRow = [2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 124];
    private static readonly uint[] TopRow = [16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27];
    private static readonly uint[] HomeRow = [30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 43];
    private static readonly uint[] BottomRow = [44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 89];

    public static KeyboardLayout Build(XkbKeymap keymap, uint layoutIndex, KeyboardLayout symbols, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(keymap);
        ArgumentNullException.ThrowIfNull(symbols);
        var rows = new KeyCell[KeyGrid.DefaultRows - 1][];
        rows[0] = Row(keymap, layoutIndex, symbols, 0, NumberRow, leading: null, trailing: null);
        rows[1] = Row(keymap, layoutIndex, symbols, 1, TopRow, leading: null, trailing: new KeyCell(1, KeyCap.Backspace, null, null, KeyClass.Function));
        rows[2] = Row(keymap, layoutIndex, symbols, 2, HomeRow, leading: null, trailing: new KeyCell(1.5f, KeyCap.Return, null, null, KeyClass.Function));
        rows[3] = Row(
            keymap, layoutIndex, symbols, 3, BottomRow,
            leading: new KeyCell(1, KeyCap.Shift, null, null, KeyClass.Function),
            trailing: new KeyCell(1, KeyCap.Shift, null, null, KeyClass.Function));
        var bottoms = new KeyCell[3][];
        foreach (var bottom in Enum.GetValues<SoftKeyboard.BottomRow>())
        {
            bottoms[(int)bottom] = symbols.Bottom(bottom);
        }

        return new KeyboardLayout(
            KeyboardLayouts.Xkb, keymap.GetLayoutName(layoutIndex) ?? "xkb", language ?? LanguageOf(keymap, layoutIndex),
            symbols.WidthUnits, numLock: false, symbols.SymbolLabel, rows, bottoms, symbols.DotCom)
        {
            LabelOverride = "Xkb",
        };
    }

    private static readonly uint[] FullNumberRow = [41, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13];
    private static readonly uint[] FullTopRow = [16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 43];
    private static readonly uint[] FullHomeRow = [30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40];
    private static readonly uint[] FullBottomRow = [44, 45, 46, 47, 48, 49, 50, 51, 52, 53];

    public static KeyboardLayout Standard(XkbKeymap keymap, uint layoutIndex, KeyboardLayout? template = null, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(keymap);
        template ??= KeyboardLayouts.Load("win8-standard-en-us") ?? throw new InvalidOperationException("win8-standard-en-us is not embedded");
        return Shape(keymap, layoutIndex, template, "xkb-standard", language, [TopRow, HomeRow, BottomRow], lettersOnly: true);
    }

    public static KeyboardLayout Full(XkbKeymap keymap, uint layoutIndex, KeyboardLayout? template = null, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(keymap);
        template ??= KeyboardLayouts.Load("win8-full-en-us") ?? throw new InvalidOperationException("win8-full-en-us is not embedded");
        return Shape(keymap, layoutIndex, template, "xkb-full", language, [FullNumberRow, FullTopRow, FullHomeRow, FullBottomRow], lettersOnly: false);
    }

    private static KeyboardLayout Shape(
        XkbKeymap keymap, uint layout, KeyboardLayout template, string id, string? language, uint[][] keyRows, bool lettersOnly)
    {
        var generated = new KeyCell[keyRows.Length][];
        for (var i = 0; i < keyRows.Length; i++)
        {
            generated[i] = Keys(keymap, layout, keyRows[i], lettersOnly);
        }

        var reference = new string[keyRows.Length];
        var next = 0;
        for (var row = 0; row < template.GridRows - 1 && next < keyRows.Length; row++)
        {
            if (Run(template.Row(row), lettersOnly, out _, out _))
            {
                reference[next++] = Signature(template.Row(row));
            }
        }

        KeyCell[] Map(KeyCell[] cells)
        {
            var match = Array.IndexOf(reference, Signature(cells));
            if (match < 0 || !Run(cells, lettersOnly, out var first, out var last) || generated[match].Length == 0)
            {
                return cells;
            }

            var result = new KeyCell[cells.Length];
            Array.Fill(result, KeyCell.Empty);
            var column = 0;
            for (var i = 0; i < first; i++)
            {
                result[column++] = cells[i];
            }

            var trailing = 0;
            for (var i = last + 1; i < cells.Length; i++)
            {
                trailing += cells[i].Weight != 0 ? 1 : 0;
            }

            foreach (var key in generated[match])
            {
                if (column >= result.Length - trailing)
                {
                    break;
                }

                result[column++] = key;
            }

            for (var i = last + 1; i < cells.Length && column < result.Length; i++)
            {
                if (cells[i].Weight != 0)
                {
                    result[column++] = cells[i];
                }
            }

            return result;
        }

        return template.MapRows(id, keymap.GetLayoutName(layout) ?? id, language ?? LanguageOf(keymap, layout), "Xkb", Map);
    }

    private static bool Run(KeyCell[] cells, bool lettersOnly, out int first, out int last)
    {
        first = -1;
        last = -1;
        for (var i = 0; i < cells.Length; i++)
        {
            if (cells[i].Plain is { IsText: true } cap && !cap.IsSpace && (!lettersOnly || IsLetterText(cap)))
            {
                if (first < 0)
                {
                    first = i;
                }

                last = i;
            }
            else if (first >= 0 && cells[i].Weight != 0)
            {
                break;
            }
        }

        return first >= 0;
    }

    private static bool IsLetterText(KeyCap cap) => cap.Text.Length == 1 && char.IsLetter(cap.Text[0]);

    private static string Signature(KeyCell[] cells) =>
        string.Join('\u001f', cells.Where(static c => c.Weight != 0).Select(static c => c.Plain?.Name ?? "_"));

    private static KeyCell[] Keys(XkbKeymap keymap, uint layout, uint[] keycodes, bool lettersOnly)
    {
        var keys = new List<KeyCell>(keycodes.Length);
        foreach (var keycode in keycodes)
        {
            var plain = Level(keymap, keycode, layout, 0);
            if (plain is null)
            {
                continue;
            }

            var shifted = Level(keymap, keycode, layout, 1);
            var letter = plain.Length == 1 && char.IsLetter(plain[0]) &&
                shifted is not null && shifted == plain.ToUpper(CultureInfo.InvariantCulture);
            if (lettersOnly && !(plain.Length == 1 && char.IsLetter(plain[0])))
            {
                continue;
            }

            var plainCap = KeyCap.Character(plain, null, letter);
            var alternate = letter || shifted is null ? plainCap : KeyCap.Character(shifted);
            var longPress = new List<KeyCap>(3);
            for (var level = 2u; level < 4; level++)
            {
                if (Level(keymap, keycode, layout, level) is { } extra && extra != plain && extra != shifted)
                {
                    longPress.Add(KeyCap.Character(extra));
                }
            }

            if (longPress.Count > 0)
            {
                longPress.Insert(0, plainCap);
            }

            keys.Add(new KeyCell(1, plainCap, alternate, longPress.Count > 0 ? [.. longPress] : null, letter ? KeyClass.Letter : KeyClass.Punctuation));
        }

        return [.. keys];
    }

    private static KeyCell[] Row(
        XkbKeymap keymap, uint layout, KeyboardLayout symbols, int row, uint[] keycodes, KeyCell? leading, KeyCell? trailing)
    {
        var cells = new KeyCell[KeyGrid.DefaultColumns];
        Array.Fill(cells, KeyCell.Empty);
        var column = 0;
        if (leading is not null)
        {
            cells[column++] = leading;
        }

        var reference = symbols.Row(row);
        for (var i = 0; i < keycodes.Length && column < KeyGrid.DefaultColumns - (trailing is null ? 0 : 1); i++)
        {
            var plain = Level(keymap, keycodes[i], layout, 0);
            if (plain is null)
            {
                continue;
            }

            var shifted = Level(keymap, keycodes[i], layout, 1);
            var letter = plain.Length == 1 && char.IsLetter(plain[0]) &&
                shifted is not null && shifted == plain.ToUpper(CultureInfo.InvariantCulture);
            var plainCap = KeyCap.Character(plain, null, letter);
            KeyCap? alternate;
            if (letter)
            {
                alternate = SymbolAt(reference, column - (leading is null ? 0 : 1)) ?? plainCap;
            }
            else
            {
                alternate = shifted is null ? plainCap : KeyCap.Character(shifted);
            }

            var longPress = new List<KeyCap>(3);
            for (var level = 2u; level < 4; level++)
            {
                if (Level(keymap, keycodes[i], layout, level) is { } extra && extra != plain && extra != shifted)
                {
                    longPress.Add(KeyCap.Character(extra));
                }
            }

            if (longPress.Count > 0)
            {
                longPress.Insert(0, plainCap);
            }

            var keyClass = row == 0 ? KeyClass.NumberRow : letter ? KeyClass.Letter : KeyClass.Punctuation;
            cells[column++] = new KeyCell(1, plainCap, alternate, longPress.Count > 0 ? [.. longPress] : null, keyClass);
        }

        if (trailing is not null)
        {
            cells[column] = trailing;
        }

        return cells;
    }

    private static KeyCap? SymbolAt(KeyCell[] reference, int column)
    {
        var visible = 0;
        foreach (var cell in reference)
        {
            if (!cell.IsVisible || cell.Plain is not { IsLetter: true })
            {
                continue;
            }

            if (visible++ == column)
            {
                return cell.Alternate;
            }
        }

        return null;
    }

    private static string? Level(XkbKeymap keymap, uint evdev, uint layout, uint level)
    {
        var keycode = evdev + 8;
        if (keycode < keymap.MinKeycode || keycode > keymap.MaxKeycode || level >= keymap.GetNumLevelsForKey(keycode, layout))
        {
            return null;
        }

        var syms = keymap.GetKeySymsByLevel(keycode, layout, level);
        if (syms.Length != 1)
        {
            return null;
        }

        var utf32 = syms[0].Utf32;
        if (utf32 < 0x20 || utf32 == 0x7f || !Rune.IsValid((int)utf32))
        {
            return null;
        }

        var rune = new Rune((int)utf32);
        return Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.NonSpacingMark
            ? null
            : rune.ToString();
    }

    private static string LanguageOf(XkbKeymap keymap, uint layout)
    {
        var name = keymap.GetLayoutName(layout) ?? string.Empty;
        foreach (var (prefix, code) in Names)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return code;
            }
        }

        return "xkb";
    }

    private static readonly (string Prefix, string Code)[] Names =
    [
        ("English", "en"), ("French", "fr"), ("German", "de"), ("Swedish", "sv"), ("Spanish", "es"),
        ("Italian", "it"), ("Portuguese", "pt"), ("Dutch", "nl"), ("Danish", "da"), ("Norwegian", "nb"),
        ("Finnish", "fi"), ("Polish", "pl"), ("Czech", "cs"), ("Russian", "ru"), ("Japanese", "ja"),
    ];
}
