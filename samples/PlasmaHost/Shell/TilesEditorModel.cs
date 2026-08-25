using System.Collections.ObjectModel;
using Avalonia.Media;

namespace PlasmaHost.Shell;

public sealed class TilesEditorModel : BreezeModel
{
    private ShellBrushes _brushes = new(BreezePalette.Fallback);

    public ObservableCollection<TileBoxModel> Tiles { get; } = [];

    public string Hint =>
        "Drag a split to resize. Middle-click a tile to split it. Right-click to merge. Escape saves.";

    public ShellBrushes Brushes
    {
        get => _brushes;
        set
        {
            if (!Set(ref _brushes, value))
            {
                return;
            }

            Raise(nameof(Backdrop));
            Raise(nameof(TileFill));
            Raise(nameof(TileOutline));
            Raise(nameof(Foreground));
        }
    }

    public IBrush Backdrop => _brushes.TilesBackdrop;

    public IBrush TileFill => _brushes.TileFill;

    public IBrush TileOutline => _brushes.Highlight;

    public IBrush Foreground => _brushes.Foreground;
}
