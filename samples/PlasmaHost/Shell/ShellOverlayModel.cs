using System.Collections.ObjectModel;
using Avalonia.Media;

namespace PlasmaHost.Shell;

public sealed class ShellOverlayModel : BreezeModel
{
    private ShellBrushes _brushes = new(BreezePalette.Fallback);
    private bool _showDesktops;
    private string _filter = string.Empty;

    public ObservableCollection<ShellCellModel> Cells { get; } = [];

    public ObservableCollection<ShellDesktopModel> Desktops { get; } = [];

    public bool ShowDesktops
    {
        get => _showDesktops;
        set => Set(ref _showDesktops, value);
    }

    public string Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value))
            {
                Raise(nameof(FilterText));
                Raise(nameof(HasFilter));
            }
        }
    }

    public string FilterText => _filter.Length == 0 ? "Type to filter" : _filter;

    public bool HasFilter => _filter.Length > 0;

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
            Raise(nameof(Foreground));
            Raise(nameof(Dim));
            Raise(nameof(Highlight));
            Raise(nameof(CellOutline));
            Raise(nameof(DesktopFill));
        }
    }

    public IBrush Backdrop => _brushes.OverlayBackdrop;

    public IBrush Foreground => _brushes.Foreground;

    public IBrush Dim => _brushes.Dim;

    public IBrush Highlight => _brushes.Highlight;

    public IBrush CellOutline => _brushes.Outline;

    public IBrush DesktopFill => _brushes.Fill;
}
