using System.Globalization;
using Basin.Config;
using Basin.Diagnostics;
using Tomlyn;
using Tomlyn.Model;

namespace Basin.SoftKeyboard;

/// <summary>
/// Loads the keyboard layouts: the built-in TOML files embedded in the library, and the user's files that shadow them.
/// </summary>
public static class KeyboardLayouts
{
    /// <summary>
    /// The id of the layout that <see cref="XkbGrid.Build"/> makes from the seat's xkb keymap.
    /// </summary>
    public const string Xkb = "xkb";

    private static readonly string[] BuiltIn = ["us-qwerty", "us-dvorak", "se-qwerty", "se-dvorak", "fr-azerty", "de-qwertz"];

    private static readonly string[] Windows8BuiltIn = ["win8-standard-en-us", "win8-thumb-en-us", "win8-full-en-us"];

    /// <summary>
    /// Gets the ids of the six built-in LunaCE layouts.
    /// </summary>
    public static IReadOnlyList<string> Ids => BuiltIn;

    /// <summary>
    /// Gets the ids of the three built-in Windows 8 layouts.
    /// </summary>
    public static IReadOnlyList<string> Windows8Ids => Windows8BuiltIn;

    /// <summary>
    /// Tests whether an id names a built-in layout.
    /// </summary>
    /// <param name="id">The layout id.</param>
    /// <returns><see langword="true"/> for a LunaCE or a Windows 8 layout.</returns>
    public static bool IsBuiltIn(string id) => Array.IndexOf(BuiltIn, id) >= 0 || Array.IndexOf(Windows8BuiltIn, id) >= 0;

    /// <summary>
    /// Picks the built-in layout for a locale: <c>fr</c> gives AZERTY, <c>de</c> gives QWERTZ, <c>sv</c> gives Swedish QWERTY, and anything else gives US QWERTY.
    /// </summary>
    /// <param name="locale">The locale, for example <c>de_DE.UTF-8</c>, or <see langword="null"/>.</param>
    /// <returns>The layout id.</returns>
    public static string DefaultFor(string? locale)
    {
        if (string.IsNullOrEmpty(locale))
        {
            return "us-qwerty";
        }

        if (locale.StartsWith("fr", StringComparison.OrdinalIgnoreCase))
        {
            return "fr-azerty";
        }

        if (locale.StartsWith("de", StringComparison.OrdinalIgnoreCase))
        {
            return "de-qwertz";
        }

        if (locale.StartsWith("sv", StringComparison.OrdinalIgnoreCase))
        {
            return "se-qwerty";
        }

        return "us-qwerty";
    }

    /// <summary>
    /// Makes a language key label from a language code: the part before a dash or an underscore, with a capital first letter.
    /// </summary>
    /// <param name="language">The language code, for example <c>en-US</c>.</param>
    /// <returns>The label, for example <c>En</c>, or an empty string for an empty code.</returns>
    public static string LanguageLabel(string language)
    {
        if (string.IsNullOrEmpty(language))
        {
            return string.Empty;
        }

        var dash = language.IndexOfAny(['-', '_']);
        var code = dash > 0 ? language[..dash] : language;
        return char.ToUpperInvariant(code[0]) + code[1..].ToLowerInvariant();
    }

    /// <summary>
    /// Loads a layout. A file named <c>ID.toml</c> in the user directory shadows the built-in layout. A user file that cannot be read or parsed is logged, and the built-in layout loads.
    /// </summary>
    /// <param name="id">The layout id.</param>
    /// <param name="userDirectory">The user's layout directory, or <see langword="null"/> for the built-in layouts only.</param>
    /// <param name="log">The logger for problems in the file, or <see langword="null"/> for the library's logger.</param>
    /// <returns>The layout, or <see langword="null"/> when no file has the id.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is <see langword="null"/> or empty.</exception>
    public static KeyboardLayout? Load(string id, string? userDirectory = null, BasinLogger? log = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        var logger = log ?? SoftKeyboardLog.Log;
        if (userDirectory is not null)
        {
            var path = Path.Combine(userDirectory, id + ".toml");
            if (File.Exists(path))
            {
                try
                {
                    return Parse(File.ReadAllText(path), id, logger, path);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or TomlException or FormatException)
                {
                    logger.Warn(error, $"layouts: cannot load {path}, the built-in layout is used");
                }
            }
        }

        using var stream = typeof(KeyboardLayouts).Assembly.GetManifestResourceStream("layouts/" + id + ".toml");
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), id, logger, "layouts/" + id + ".toml");
    }

    /// <summary>
    /// Parses a layout file. An unknown key in the file is logged. A layout with an emoji key gets the built-in emoji pages.
    /// </summary>
    /// <param name="text">The TOML text.</param>
    /// <param name="id">The layout id. An <c>id</c> in the file that differs is logged and ignored.</param>
    /// <param name="log">The logger for problems in the file, or <see langword="null"/> for the library's logger.</param>
    /// <param name="source">The file name for messages, or <see langword="null"/>.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The file does not describe a valid layout.</exception>
    /// <exception cref="TomlException">The text is not valid TOML.</exception>
    public static KeyboardLayout Parse(string text, string id, BasinLogger? log = null, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var logger = log ?? SoftKeyboardLog.Log;
        var table = Toml.ToModel(text, source);
        var top = new TomlReader(table, logger);
        var name = top.Text("name") ?? id;
        var fileId = top.Text("id");
        if (fileId is not null && fileId != id)
        {
            logger.Warn($"layouts: {source} id '{fileId}' does not match the file name, '{id}' is used");
        }

        var language = top.Text("language") ?? "en";
        var units = (float)top.Number("width_units", 11.0);
        var numLock = top.Flag("num_lock", false);
        var symbolLabel = top.Text("symbol_label") ?? "+ = [  ]";
        var gridRows = (int)top.Number("grid_rows", KeyGrid.DefaultRows);
        var gridColumns = (int)top.Number("grid_columns", KeyGrid.DefaultColumns);
        float[]? rowHeights = null;
        if (top.Raw("row_heights") is TomlArray heights)
        {
            rowHeights = new float[heights.Count];
            for (var i = 0; i < heights.Count; i++)
            {
                rowHeights[i] = heights[i] switch
                {
                    double d => (float)d,
                    long l => l,
                    _ => throw new FormatException($"In {source}, row_heights[{i}] is not a number."),
                };
            }
        }

        var shaped = rowHeights is not null || gridRows != KeyGrid.DefaultRows || gridColumns != KeyGrid.DefaultColumns;
        if (gridRows is < 2 or > KeyGrid.MaxRows || gridColumns is < 1 or > KeyGrid.MaxColumns)
        {
            throw new FormatException($"In {source}, the grid size is not valid. A layout has 2 to {KeyGrid.MaxRows} rows and 1 to {KeyGrid.MaxColumns} columns.");
        }

        if (shaped && rowHeights is null)
        {
            rowHeights = new float[gridRows];
            Array.Fill(rowHeights, 70f);
        }

        if (rowHeights is not null && rowHeights.Length != gridRows)
        {
            throw new FormatException($"In {source}, row_heights has {rowHeights.Length} entries. It must have {gridRows}.");
        }

        var bottomTable = top.Section("bottom");
        if (top.Raw("rows") is not TomlArray rowArray ||
            (rowArray.Count != gridRows - 1 && !(shaped && bottomTable is null && rowArray.Count == gridRows)))
        {
            throw new FormatException($"In {source}, rows is not valid. It must be an array of {gridRows - 1} rows.");
        }

        var rows = new KeyCell[gridRows - 1][];
        for (var row = 0; row < rows.Length; row++)
        {
            rows[row] = ParseRow(rowArray[row] as TomlArray, row, gridColumns, logger, $"{source} rows[{row}]");
        }

        var dotCom = ParseCaps(top.Raw("dotcom") as TomlArray, logger, $"{source} dotcom") ?? [];
        var bottoms = new KeyCell[3][];
        if (bottomTable is null)
        {
            if (rowArray.Count != gridRows)
            {
                throw new FormatException($"In {source}, the [bottom] table is missing.");
            }

            bottoms[0] = bottoms[1] = bottoms[2] = ParseRow(rowArray[gridRows - 1] as TomlArray, gridRows - 1, gridColumns, logger, $"{source} rows[{gridRows - 1}]");
        }
        else
        {
            bottoms[(int)BottomRow.Default] = ParseRow(bottomTable.Raw("default") as TomlArray, gridRows - 1, gridColumns, logger, $"{source} bottom.default");
            bottoms[(int)BottomRow.Email] = bottomTable.Raw("email") is TomlArray email
                ? ParseRow(email, gridRows - 1, gridColumns, logger, $"{source} bottom.email")
                : bottoms[(int)BottomRow.Default];
            bottoms[(int)BottomRow.Url] = bottomTable.Raw("url") is TomlArray url
                ? ParseRow(url, gridRows - 1, gridColumns, logger, $"{source} bottom.url")
                : bottoms[(int)BottomRow.Default];
        }

        Dictionary<string, KeyCell[][]>? pages = null;
        if (top.Section("pages") is { } pageTable)
        {
            pages = new Dictionary<string, KeyCell[][]>(StringComparer.Ordinal);
            foreach (var pageName in pageTable.Table.Keys.ToArray())
            {
                if (pageTable.Section(pageName) is not { } page)
                {
                    throw new FormatException($"In {source}, pages.{pageName} must be a table.");
                }

                if (page.Raw("rows") is not TomlArray pageRows || pageRows.Count != gridRows)
                {
                    throw new FormatException($"In {source}, pages.{pageName}.rows must be an array of {gridRows} rows.");
                }

                var cells = new KeyCell[gridRows][];
                for (var row = 0; row < gridRows; row++)
                {
                    cells[row] = ParseRow(pageRows[row] as TomlArray, row, gridColumns, logger, $"{source} pages.{pageName}.rows[{row}]");
                }

                pages[pageName] = cells;
            }
        }

        top.ReportUnknown();
        var layout = new KeyboardLayout(id, name, language, units, numLock, symbolLabel, gridRows, gridColumns, rowHeights, rows, bottoms, dotCom, pages);
        return HasEmojiKey(rows, bottoms, pages)
            ? layout.WithPages(EmojiPages.Build(gridRows, gridColumns, EmojiPages.BuiltIn))
            : layout;
    }

    internal static bool HasEmojiKey(KeyCell[][] rows, KeyCell[][] bottoms, Dictionary<string, KeyCell[][]>? pages)
    {
        static bool Any(KeyCell[][] cells) => cells.Any(static row => row.Any(static cell => cell.Plain?.Function == KeyFunction.Emoji));
        return Any(rows) || Any(bottoms) || (pages is not null && pages.Values.Any(Any));
    }

    private static KeyCell[] ParseRow(TomlArray? array, int row, int columns, BasinLogger log, string where)
    {
        if (array is null)
        {
            throw new FormatException($"In {where}, the row must be an array of keys.");
        }

        if (array.Count > columns)
        {
            throw new FormatException($"In {where}, the row has {array.Count} keys. A row has at most {columns}.");
        }

        var cells = new KeyCell[columns];
        Array.Fill(cells, KeyCell.Empty);
        for (var column = 0; column < array.Count; column++)
        {
            if (array[column] is not TomlTable table)
            {
                throw new FormatException($"In {where}[{column}], the key must be an inline table.");
            }

            var reader = new TomlReader(table, log);
            var weight = (float)reader.Number("w", 1.0);
            var plainToken = reader.Text("plain");
            var plainRaw = reader.Raw("plain");
            if (plainRaw is string literal && literal.Length > 0)
            {
                plainToken = literal;
            }

            if (reader.Flag("spacer", false))
            {
                reader.ReportUnknown();
                cells[column] = weight > 0 ? KeyCell.Spacer(weight) : throw new FormatException($"In {where}[{column}], the spacer weight must be more than 0.");
                continue;
            }

            if (weight == 0 || plainToken is null)
            {
                reader.ReportUnknown();
                continue;
            }

            var label = reader.Text("label");
            if (!KeyCap.TryParse(plainToken, out var plain) || plain is null)
            {
                throw new FormatException($"In {where}[{column}], the key '{plainToken}' is not known.");
            }

            if (label is not null)
            {
                plain = plain.WithLabel(label);
            }

            KeyCap? alternate = null;
            if (reader.Raw("alt") is string altToken && altToken.Length > 0)
            {
                if (!KeyCap.TryParse(altToken, out alternate))
                {
                    throw new FormatException($"In {where}[{column}], the alternate '{altToken}' is not known.");
                }
            }

            var longPress = ParseCaps(reader.Raw("long") as TomlArray, log, $"{where}[{column}].long");
            var keyClass = reader.Text("class") switch
            {
                "letter" => KeyClass.Letter,
                "punctuation" => KeyClass.Punctuation,
                "function" => KeyClass.Function,
                "number" => KeyClass.NumberRow,
                null => ClassOf(plain, row),
                var other => throw new FormatException($"In {where}[{column}], the class '{other}' is not known."),
            };
            reader.ReportUnknown();
            cells[column] = new KeyCell(weight, plain, alternate, longPress, keyClass);
        }

        return cells;
    }

    private static KeyCap[]? ParseCaps(TomlArray? array, BasinLogger log, string where)
    {
        if (array is null)
        {
            return null;
        }

        var caps = new List<KeyCap>(array.Count);
        foreach (var item in array)
        {
            if (item is string token && KeyCap.TryParse(token, out var cap) && cap is not null)
            {
                caps.Add(cap);
            }
            else
            {
                log.Warn($"layouts: '{item}' in {where} is not a key, it is ignored");
            }
        }

        return [.. caps];
    }

    private static KeyClass ClassOf(KeyCap cap, int row)
    {
        if (!cap.IsText)
        {
            return KeyClass.Function;
        }

        if (row == 0)
        {
            return KeyClass.NumberRow;
        }

        return cap.IsLetter || cap.IsSpace || (cap.Text.Length == 1 && char.IsLetter(cap.Text[0]))
            ? KeyClass.Letter
            : KeyClass.Punctuation;
    }

    internal static string Format(float value) => value.ToString(CultureInfo.InvariantCulture);
}
