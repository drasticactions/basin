namespace Basin.SoftKeyboard;

public sealed class KeyboardLayout
{
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

    public const string LockedSymbolLabel = "A B C";

    public string Id { get; }

    public string Name { get; }

    public string Language { get; }

    public float WidthUnits { get; }

    public bool NumLock { get; }

    public string SymbolLabel { get; }

    public int GridRows { get; }

    public int GridColumns { get; }

    public bool IsShaped => _rowHeights is not null;

    public IReadOnlyCollection<string> PageNames => _pages.Keys;

    public IReadOnlyList<KeyCap> DotCom { get; }

    public string LanguageLabel => LabelOverride ?? KeyboardLayouts.LanguageLabel(Language);

    internal string? LabelOverride { get; init; }

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
