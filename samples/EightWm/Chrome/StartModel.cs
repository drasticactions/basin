using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;
using AvaWin.Controls;

namespace EightWm;

public sealed class StartModel : ObservableModel
{
    private IBrush _background = Brushes.Black;
    private bool _zoomedOut;
    private string _filter = string.Empty;
    private AppsSort _appsSort;
    private Func<object?, object?>? _appsGroupKey;

    public ObservableCollection<Tile> Tiles { get; } = [];

    public ObservableCollection<TileGroupModel> Groups { get; } = [];

    public ObservableCollection<Tile> Apps { get; } = [];

    public ObservableCollection<Tile> FilteredApps { get; } = [];

    public Func<object?, object?> GroupKey { get; } = tile => (tile as Tile)?.Group;

    public Func<object?, object?> GroupsKey { get; } = group => (group as TileGroupModel)?.Name;

    public ICommand SortCommand => _sortCommand ??= new RelayCommand(parameter =>
    {
        if (parameter is AppsSort sort)
        {
            AppsSort = sort;
        }
        else if (parameter is string text && Enum.TryParse<AppsSort>(text, out var parsed))
        {
            AppsSort = parsed;
        }
    });

    private ICommand? _sortCommand;

    public event Action<AppsSort>? AppsSortChanged;

    public AppsSort AppsSort
    {
        get => _appsSort;
        set
        {
            if (!Set(ref _appsSort, value))
            {
                return;
            }

            Raise(nameof(AppsSortLabel));
            AppsGroupKey = value == AppsSort.Category ? GroupKey : null;
            ApplyFilter();
            AppsSortChanged?.Invoke(value);
        }
    }

    public string AppsSortLabel => _appsSort switch
    {
        AppsSort.DateInstalled => "by date installed",
        AppsSort.MostUsed => "by most used",
        AppsSort.Category => "by category",
        _ => "by name",
    };

    public Func<object?, object?>? AppsGroupKey
    {
        get => _appsGroupKey;
        private set => Set(ref _appsGroupKey, value);
    }

    public ICommand ShowAppsCommand => _showAppsCommand ??= new RelayCommand(_ => RequestApps(true));

    public ICommand HideAppsCommand => _hideAppsCommand ??= new RelayCommand(_ => RequestApps(false));

    private ICommand? _showAppsCommand;
    private ICommand? _hideAppsCommand;

    public event Action<Tile>? TileInvoked;

    public event Action<bool>? AppsRequested;

    public event Action<bool>? ZoomChanged;

    public IBrush Background
    {
        get => _background;
        set => Set(ref _background, value);
    }

    public bool ZoomedOut
    {
        get => _zoomedOut;
        set
        {
            if (Set(ref _zoomedOut, value))
            {
                ZoomChanged?.Invoke(value);
            }
        }
    }

    public string Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value))
            {
                ApplyFilter();
            }
        }
    }

    public GridItemInfo ItemInfo(int index) =>
        index >= 0 && index < Tiles.Count
            ? new GridItemInfo(Tile.SpanOf(Tiles[index].Size).Width, Tile.SpanOf(Tiles[index].Size).Height)
            : new GridItemInfo(Tile.Cell, Tile.Cell);

    public void SetTiles(IEnumerable<Tile> tiles, IReadOnlyList<string> groupOrder)
    {
        var ordered = new List<(string Group, List<Tile> Tiles)>();
        foreach (var tile in tiles)
        {
            var slot = ordered.FindIndex(entry => entry.Group == tile.Group);
            if (slot < 0)
            {
                ordered.Add((tile.Group, [tile]));
            }
            else
            {
                ordered[slot].Tiles.Add(tile);
            }
        }

        if (groupOrder.Count > 0)
        {
            ordered.Sort((left, right) =>
            {
                var a = Rank(groupOrder, left.Group);
                var b = Rank(groupOrder, right.Group);
                return a != b ? a.CompareTo(b) : string.CompareOrdinal(left.Group, right.Group);
            });
        }

        Tiles.Clear();
        Groups.Clear();
        foreach (var (group, members) in ordered)
        {
            foreach (var tile in members)
            {
                Tiles.Add(tile);
            }

            Groups.Add(new TileGroupModel(group, members.Count));
        }
    }

    public void SetApps(IEnumerable<Tile> apps)
    {
        Apps.Clear();
        foreach (var app in apps)
        {
            Apps.Add(app);
        }

        ApplyFilter();
    }

    public void RequestApps(bool visible) => AppsRequested?.Invoke(visible);

    public void Invoke(Tile tile) => TileInvoked?.Invoke(tile);

    private void ApplyFilter()
    {
        IEnumerable<Tile> matching = Apps.Where(app =>
            _filter.Length == 0 || app.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        matching = _appsSort switch
        {
            AppsSort.Name => matching.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase),
            AppsSort.DateInstalled => matching.OrderByDescending(app => app.Installed).ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase),
            AppsSort.MostUsed => matching.OrderByDescending(app => app.Launches).ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => matching,
        };

        FilteredApps.Clear();
        foreach (var app in matching)
        {
            FilteredApps.Add(app);
        }
    }

    public void Resort()
    {
        if (_appsSort == AppsSort.MostUsed)
        {
            ApplyFilter();
        }
    }

    private static int Rank(IReadOnlyList<string> order, string name)
    {
        for (var i = 0; i < order.Count; i++)
        {
            if (order[i] == name)
            {
                return i;
            }
        }

        return int.MaxValue;
    }
}
