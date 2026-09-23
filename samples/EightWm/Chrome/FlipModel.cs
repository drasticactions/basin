using Avalonia;

namespace EightWm;

public sealed class FlipModel : ObservableModel
{
    private Tile? _tile;
    private bool _showName;
    private double _iconSize;
    private Thickness _iconMargin;

    public Tile? Tile
    {
        get => _tile;
        private set => Set(ref _tile, value);
    }

    public bool ShowName
    {
        get => _showName;
        private set => Set(ref _showName, value);
    }

    public double IconSize
    {
        get => _iconSize;
        private set => Set(ref _iconSize, value);
    }

    public Thickness IconMargin
    {
        get => _iconMargin;
        private set => Set(ref _iconMargin, value);
    }

    public void ShowTile(Tile tile)
    {
        Tile = tile;
        ShowName = true;
        IconSize = tile.IconSize;
        IconMargin = new Thickness(0, 0, 0, 21);
    }

    public void ShowEntry(Tile entry)
    {
        Tile = entry;
        ShowName = false;
        IconSize = Shell.AppIconSize;
        IconMargin = default;
    }
}
