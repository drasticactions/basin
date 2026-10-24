namespace Basin.SoftKeyboard;

/// <summary>
/// The main grid of a layout and its named pages. A page key shows a page in place of the main grid.
/// </summary>
public sealed class KeyPageSet
{
    /// <summary>
    /// The name of the main grid in the set.
    /// </summary>
    public const string MainPage = "main";

    private readonly Dictionary<string, KeyGrid> _pages;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyPageSet"/> class. A page named <see cref="MainPage"/> in <paramref name="pages"/> is ignored.
    /// </summary>
    /// <param name="main">The main grid.</param>
    /// <param name="pages">The named pages, or <see langword="null"/> for none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="main"/> or a page grid is <see langword="null"/>.</exception>
    public KeyPageSet(KeyGrid main, IReadOnlyDictionary<string, KeyGrid>? pages = null)
    {
        ArgumentNullException.ThrowIfNull(main);
        Main = main;
        _pages = new Dictionary<string, KeyGrid>(StringComparer.Ordinal) { [MainPage] = main };
        if (pages is not null)
        {
            foreach (var (name, grid) in pages)
            {
                ArgumentNullException.ThrowIfNull(grid);
                if (name != MainPage)
                {
                    _pages[name] = grid;
                }
            }
        }
    }

    /// <summary>
    /// Gets the grid that shows when no page is selected.
    /// </summary>
    public KeyGrid Main { get; }

    /// <summary>
    /// Gets the names of every grid in the set, <see cref="MainPage"/> included.
    /// </summary>
    public IReadOnlyCollection<string> Names => _pages.Keys;

    /// <summary>
    /// Finds a grid by its page name. The match is ordinal.
    /// </summary>
    /// <param name="name">The page name.</param>
    /// <param name="grid">The grid when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the set has the page.</returns>
    public bool TryGet(string name, out KeyGrid grid) => _pages.TryGetValue(name, out grid!);
}
