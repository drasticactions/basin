namespace Basin.SoftKeyboard;

/// <summary>
/// A keyboard layout as data: the key rows, the bottom-row variants for each field kind, and the named pages. It builds a <see cref="KeyGrid"/> on request.
/// </summary>
public sealed class KeyboardLayout
{
    /// <summary>
    /// The column of the Symbol key in the bottom row. The language key takes one unit from it.
    /// </summary>
    public const int SymbolColumn = 1;

    private readonly KeyCell[][] _rows;
    private readonly KeyCell[][] _bottoms;
    private readonly float[]? _rowHeights;
    private readonly Dictionary<string, KeyCell[][]> _pages;

    internal KeyboardLayout(
        string id, string name, string language, float widthUnits, bool numLock, string symbolLabel,
        KeyCell[][] rows, KeyCell[][] bottoms, IReadOnlyList<KeyCap> dotCom)
        : this(id, name, language, widthUnits, numLock, symbolLabel, KeyGrid.DefaultRows, KeyGrid.DefaultColumns, null, rows, bottoms, dotCom, null)
    {
    }

    internal KeyboardLayout(
        string id, string name, string language, float widthUnits, bool numLock, string symbolLabel,
        int gridRows, int gridColumns, float[]? rowHeights,
        KeyCell[][] rows, KeyCell[][] bottoms, IReadOnlyList<KeyCap> dotCom,
        IReadOnlyDictionary<string, KeyCell[][]>? pages)
    {
        Id = id;
        Name = name;
        Language = language;
        WidthUnits = widthUnits;
        NumLock = numLock;
        SymbolLabel = symbolLabel;
        GridRows = gridRows;
        GridColumns = gridColumns;
        _rowHeights = rowHeights;
        _rows = rows;
        _bottoms = bottoms;
        DotCom = dotCom;
        _pages = pages is null ? new(StringComparer.Ordinal) : new(pages, StringComparer.Ordinal);
    }

    /// <summary>
    /// The label for the Symbol key while the symbol lock is on. The library does not use it. A view can use it.
    /// </summary>
    public const string LockedSymbolLabel = "A B C";

    /// <summary>
    /// Gets the layout id, for example <c>us-qwerty</c>.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the display name. It is the id when the file names none.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the language code, for example <c>en</c> or <c>sv</c>.
    /// </summary>
    public string Language { get; }

    /// <summary>
    /// Gets the width of the keyboard in key units, which is the sum of the bottom row.
    /// </summary>
    public float WidthUnits { get; }

    /// <summary>
    /// Gets a value indicating whether the number row gives digits without Shift while Shift is locked, as AZERTY does.
    /// </summary>
    public bool NumLock { get; }

    /// <summary>
    /// Gets the label for the Symbol key while the symbol lock is off.
    /// </summary>
    public string SymbolLabel { get; }

    /// <summary>
    /// Gets the number of rows of each grid, the bottom row included.
    /// </summary>
    public int GridRows { get; }

    /// <summary>
    /// Gets the number of columns of each grid.
    /// </summary>
    public int GridColumns { get; }

    /// <summary>
    /// Gets a value indicating whether the layout has its own shape. A shaped layout builds grids with <see cref="KeyGrid.RowBias"/> off and has no language key.
    /// </summary>
    public bool IsShaped => _rowHeights is not null;

    /// <summary>
    /// Gets the names of the layout's pages. The collection is empty for a layout without pages.
    /// </summary>
    public IReadOnlyCollection<string> PageNames => _pages.Keys;

    /// <summary>
    /// Gets the long-press list of the <c>.com</c> key for the layout's country.
    /// </summary>
    public IReadOnlyList<KeyCap> DotCom { get; }

    /// <summary>
    /// Gets the language key's label: the language code with a capital first letter, or the layout's own label.
    /// </summary>
    public string LanguageLabel => LabelOverride ?? KeyboardLayouts.LanguageLabel(Language);

    internal string? LabelOverride { get; init; }

    /// <summary>
    /// Builds the main grid for a field kind. With two or more languages, the language key takes one unit from a Symbol key that is at least two units wide.
    /// </summary>
    /// <param name="bottom">The bottom-row variant.</param>
    /// <param name="languages">The language key's long-press list, or <see langword="null"/> for no language key.</param>
    /// <param name="languageLabel">The language key's label, or <see langword="null"/> for <see cref="LanguageLabel"/>.</param>
    /// <returns>A new grid.</returns>
    public KeyGrid Grid(BottomRow bottom = BottomRow.Default, IReadOnlyList<KeyCap>? languages = null, string? languageLabel = null)
    {
        var cells = new KeyCell[GridRows * GridColumns];
        for (var row = 0; row < GridRows - 1; row++)
        {
            Array.Copy(_rows[row], 0, cells, row * GridColumns, GridColumns);
        }

        var last = _bottoms[(int)bottom];
        Array.Copy(last, 0, cells, (GridRows - 1) * GridColumns, GridColumns);
        if (languages is { Count: >= 2 })
        {
            var symbolIndex = ((GridRows - 1) * GridColumns) + SymbolColumn;
            var symbol = cells[symbolIndex];
            if (symbol.Plain?.Function == KeyFunction.Symbol && symbol.Weight >= 2)
            {
                cells[symbolIndex] = symbol.WithWeight(symbol.Weight - 1);
                var cap = KeyCap.LanguageKey(languageLabel ?? LanguageLabel);
                cells[symbolIndex + 1] = new KeyCell(1, cap, cap, languages, KeyClass.Function);
            }
        }

        return Make(cells);
    }

    /// <summary>
    /// Builds the grid of a named page.
    /// </summary>
    /// <param name="page">The page name.</param>
    /// <returns>A new grid, or <see langword="null"/> when the layout has no such page.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="page"/> is <see langword="null"/>.</exception>
    public KeyGrid? PageGrid(string page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!_pages.TryGetValue(page, out var rows))
        {
            return null;
        }

        var cells = new KeyCell[GridRows * GridColumns];
        for (var row = 0; row < GridRows; row++)
        {
            Array.Copy(rows[row], 0, cells, row * GridColumns, GridColumns);
        }

        return Make(cells);
    }

    /// <summary>
    /// Builds the main grid for a field kind and the grid of every page.
    /// </summary>
    /// <param name="bottom">The bottom-row variant of the main grid.</param>
    /// <param name="languages">The language key's long-press list, or <see langword="null"/> for no language key.</param>
    /// <param name="languageLabel">The language key's label, or <see langword="null"/> for <see cref="LanguageLabel"/>.</param>
    /// <returns>A new page set.</returns>
    public KeyPageSet PageSet(BottomRow bottom = BottomRow.Default, IReadOnlyList<KeyCap>? languages = null, string? languageLabel = null)
    {
        var pages = new Dictionary<string, KeyGrid>(StringComparer.Ordinal);
        foreach (var name in _pages.Keys)
        {
            pages[name] = PageGrid(name)!;
        }

        return new KeyPageSet(Grid(bottom, languages, languageLabel), pages);
    }

    internal KeyboardLayout WithPages(IReadOnlyDictionary<string, KeyCell[][]> pages)
    {
        var merged = new Dictionary<string, KeyCell[][]>(_pages, StringComparer.Ordinal);
        foreach (var (name, rows) in pages)
        {
            merged.TryAdd(name, rows);
        }

        return new KeyboardLayout(Id, Name, Language, WidthUnits, NumLock, SymbolLabel, GridRows, GridColumns, _rowHeights, _rows, _bottoms, DotCom, merged)
        {
            LabelOverride = LabelOverride,
        };
    }

    internal KeyboardLayout MapRows(string id, string name, string language, string? labelOverride, Func<KeyCell[], KeyCell[]> map)
    {
        var rows = new KeyCell[_rows.Length][];
        for (var row = 0; row < rows.Length; row++)
        {
            rows[row] = map(_rows[row]);
        }

        var bottoms = new KeyCell[_bottoms.Length][];
        for (var bottom = 0; bottom < bottoms.Length; bottom++)
        {
            bottoms[bottom] = map(_bottoms[bottom]);
        }

        var pages = new Dictionary<string, KeyCell[][]>(StringComparer.Ordinal);
        foreach (var (page, pageRows) in _pages)
        {
            pages[page] = [.. pageRows.Select(map)];
        }

        return new KeyboardLayout(id, name, language, WidthUnits, NumLock, SymbolLabel, GridRows, GridColumns, _rowHeights, rows, bottoms, DotCom, pages)
        {
            LabelOverride = labelOverride,
        };
    }

    internal KeyCell[] Row(int row) => row < GridRows - 1 ? _rows[row] : _bottoms[0];

    internal KeyCell[] Bottom(BottomRow bottom) => _bottoms[(int)bottom];

    internal IReadOnlyList<float>? RowHeights => _rowHeights;

    private KeyGrid Make(KeyCell[] cells) =>
        _rowHeights is null ? new KeyGrid(cells, null, WidthUnits) : new KeyGrid(GridRows, GridColumns, cells, _rowHeights, WidthUnits);
}
