namespace Basin.SoftKeyboard;

public sealed class KeyPageSet
{
    public const string MainPage = "main";

    private readonly Dictionary<string, KeyGrid> _pages;

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

    public KeyGrid Main { get; }

    public IReadOnlyCollection<string> Names => _pages.Keys;

    public bool TryGet(string name, out KeyGrid grid) => _pages.TryGetValue(name, out grid!);
}
