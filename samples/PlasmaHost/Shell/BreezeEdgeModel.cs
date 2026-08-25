using Avalonia.Media;

namespace PlasmaHost.Shell;

public sealed class BreezeEdgeModel : BreezeModel
{
    private bool _active = true;
    private BreezeBrushes _brushes = new(BreezePalette.Fallback);

    public bool Active
    {
        get => _active;
        set
        {
            if (Set(ref _active, value))
            {
                Raise(nameof(Background));
            }
        }
    }

    public BreezeBrushes Brushes
    {
        get => _brushes;
        set
        {
            if (Set(ref _brushes, value))
            {
                Raise(nameof(Background));
            }
        }
    }

    public IBrush Background => _active ? _brushes.ActiveBackground : _brushes.InactiveBackground;
}
