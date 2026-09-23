using System.ComponentModel;
using Avalonia.Controls;
using AvaWin.Animations;

namespace EightWm;

public sealed partial class TileView : UserControl
{
    private Tile? _tile;

    public TileView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_tile is not null)
        {
            _tile.PropertyChanged -= OnTileChanged;
        }

        _tile = DataContext as Tile;
        if (_tile is not null)
        {
            _tile.PropertyChanged += OnTileChanged;
        }
    }

    private void OnTileChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Tile.Badge):
                _ = WinAnimations.UpdateBadge(BadgeText);
                break;
            case nameof(Tile.Peek):
                _ = WinAnimations.UpdateBadge(PeekText);
                break;
        }
    }
}
