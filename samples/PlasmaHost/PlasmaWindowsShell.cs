using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.UI.Avalonia;
using PlasmaHost.Shell;
using Xkb;

namespace PlasmaHost;

internal sealed class PlasmaWindowsShell : IDisposable
{
    private const int BarHeight = 96;
    private const int BarGap = 16;

    private readonly ShellOverlayModel _model = new();
    private readonly PlasmaShellSurface _backdrop;
    private readonly PlasmaThumbnailGrid _grid;
    private readonly SceneTree _root;
    private readonly PlasmaHostWindows _windows;
    private readonly PlasmaHostDesktops _desktops;
    private readonly OutputLayout _layout;
    private readonly BreezeTheme _theme;
    private readonly List<PlasmaHostView> _shown = [];
    private readonly List<Box> _desktopBoxes = [];
    private string _filter = string.Empty;
    private PlasmaThumbnail? _dragging;
    private bool _disposed;

    public PlasmaWindowsShell(
        AvaloniaUIHost host,
        UISurfaceIndex index,
        SceneTree layer,
        PlasmaHostWindows windows,
        PlasmaHostDesktops desktops,
        OutputLayout layout,
        BreezeTheme theme)
    {
        _root = new SceneTree(layer) { Enabled = false };
        _backdrop = new PlasmaShellSurface(host, index, _root);
        _grid = new PlasmaThumbnailGrid(_root);
        _windows = windows;
        _desktops = desktops;
        _layout = layout;
        _theme = theme;
        _model.Brushes = theme.Shell;
    }

    public event Action? Repaint;

    public void RefreshTheme() => _model.Brushes = _theme.Shell;

    public PlasmaShellMode Mode { get; private set; }

    public bool IsOpen => Mode != PlasmaShellMode.None;

    public void Toggle(PlasmaShellMode mode)
    {
        if (Mode == mode)
        {
            Close();
            return;
        }

        Mode = mode;
        _filter = string.Empty;
        _model.Filter = string.Empty;
        _model.ShowDesktops = mode is PlasmaShellMode.Overview or PlasmaShellMode.Grid;
        Rebuild();
    }

    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        Mode = PlasmaShellMode.None;
        _dragging = null;
        _root.Enabled = false;
        _grid.Clear();
        _model.Cells.Clear();
        _model.Desktops.Clear();
        Repaint?.Invoke();
    }

    public bool Key(XkbKeysym symbol)
    {
        if (!IsOpen)
        {
            return false;
        }

        var name = symbol.Name;
        switch (name)
        {
            case "Escape":
                Close();
                return true;
            case "BackSpace":
                if (_filter.Length > 0)
                {
                    _filter = _filter[..^1];
                    Refilter();
                }

                return true;
            case "Return":
            case "KP_Enter":
                if (_shown.Count > 0)
                {
                    Activate(_shown[0]);
                }

                return true;
        }

        var code = symbol.Utf32;
        if (code >= 0x20 && code != 0x7F)
        {
            _filter += char.ConvertFromUtf32((int)code);
            Refilter();
        }

        return true;
    }

    public bool PointerMotion(double x, double y)
    {
        if (!IsOpen)
        {
            return false;
        }

        var hovered = _grid.At(x, y);
        foreach (var cell in _model.Cells)
        {
            cell.Selected = false;
        }

        if (hovered is not null && IndexOf(hovered) is { } index && index < _model.Cells.Count)
        {
            _model.Cells[index].Selected = true;
        }

        var over = DesktopAt(x, y);
        for (var i = 0; i < _model.Desktops.Count; i++)
        {
            _model.Desktops[i].Highlighted = _dragging is not null && i == over;
        }

        Repaint?.Invoke();
        return true;
    }

    public bool PointerButton(double x, double y, uint button, bool pressed)
    {
        if (!IsOpen)
        {
            return false;
        }

        if (button != InputCodes.BtnLeft)
        {
            return true;
        }

        if (pressed)
        {
            _dragging = _grid.At(x, y);
            return true;
        }

        var released = _dragging;
        _dragging = null;
        if (released is null)
        {
            if (DesktopAt(x, y) is { } target)
            {
                _desktops.Activate(target);
                Close();
                return true;
            }

            Close();
            return true;
        }

        if (DesktopAt(x, y) is { } destination)
        {
            _desktops.MoveTo(released.View, destination);
            Rebuild();
            return true;
        }

        if (ReferenceEquals(_grid.At(x, y), released))
        {
            Activate(released.View);
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _grid.Dispose();
        _backdrop.Dispose();
        _root.Destroy();
    }

    private void Activate(PlasmaHostView view)
    {
        var desktop = _desktops.IndexOf(view);
        Close();
        _desktops.Activate(desktop);
        _windows.Minimize(view, false);
        view.Tree.RaiseToTop();
        _windows.Focus(view);
        Repaint?.Invoke();
    }

    private void Refilter()
    {
        _model.Filter = _filter;
        Rebuild();
    }

    private void Rebuild()
    {
        if (!IsOpen)
        {
            return;
        }

        var bounds = _layout.Bounds;
        var scale = _layout.OutputAt(bounds.X + 1, bounds.Y + 1)?.Scale ?? 1.0;
        _backdrop.Show(bounds, scale, () => new ShellOverlayView { DataContext = _model });
        _backdrop.Visible = true;
        _root.Enabled = true;

        Collect();
        var area = _model.ShowDesktops
            ? new Box(bounds.X, bounds.Y + BarHeight + BarGap, bounds.Width, bounds.Height - BarHeight - BarGap)
            : bounds;
        _grid.Layout(_shown, area, _windows.FrameBoxOf);
        _grid.Visible = true;
        _grid.RaiseToTop();

        LayoutDesktopBar(bounds);
        SyncCells();
        Repaint?.Invoke();
    }

    private void Collect()
    {
        _shown.Clear();
        var current = _desktops.Current;
        var focusedClass = _windows.FocusedView?.Xdg.AppId;
        foreach (var view in _windows.Views)
        {
            var onDesktop = _desktops.IndexOf(view);
            var wanted = Mode switch
            {
                PlasmaShellMode.Overview => onDesktop == current,
                PlasmaShellMode.WindowsCurrent => onDesktop == current,
                PlasmaShellMode.WindowsClass => view.Xdg.AppId == focusedClass,
                _ => true,
            };
            if (!wanted || !Matches(view))
            {
                continue;
            }

            _shown.Add(view);
        }
    }

    private bool Matches(PlasmaHostView view)
    {
        if (_filter.Length == 0)
        {
            return true;
        }

        return (view.Xdg.Title?.Contains(_filter, StringComparison.OrdinalIgnoreCase) ?? false)
            || (view.Xdg.AppId?.Contains(_filter, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void LayoutDesktopBar(in Box bounds)
    {
        _desktopBoxes.Clear();
        _model.Desktops.Clear();
        if (!_model.ShowDesktops)
        {
            return;
        }

        var count = _desktops.Desktops.Count;
        var width = Math.Max(1, (bounds.Width - (BarGap * (count + 1))) / count);
        for (var i = 0; i < count; i++)
        {
            var box = new Box(bounds.X + BarGap + (i * (width + BarGap)), bounds.Y + BarGap, width, BarHeight);
            _desktopBoxes.Add(box);
            _model.Desktops.Add(new ShellDesktopModel
            {
                X = box.X - bounds.X,
                Y = box.Y - bounds.Y,
                Width = box.Width,
                Height = box.Height,
                Name = _desktops.Desktops[i].Name,
                IsCurrent = i == _desktops.Current,
            });
        }
    }

    private void SyncCells()
    {
        var bounds = _layout.Bounds;
        _model.Cells.Clear();
        foreach (var cell in _grid.Cells)
        {
            _model.Cells.Add(new ShellCellModel
            {
                X = cell.Box.X - bounds.X,
                Y = cell.Box.Y - bounds.Y,
                Width = cell.Box.Width,
                Height = cell.Box.Height,
                Title = cell.View.Xdg.Title ?? cell.View.Xdg.AppId ?? string.Empty,
            });
        }
    }

    private int? IndexOf(PlasmaThumbnail cell)
    {
        for (var i = 0; i < _grid.Cells.Count; i++)
        {
            if (ReferenceEquals(_grid.Cells[i], cell))
            {
                return i;
            }
        }

        return null;
    }

    private int? DesktopAt(double x, double y)
    {
        for (var i = 0; i < _desktopBoxes.Count; i++)
        {
            var box = _desktopBoxes[i];
            if (x >= box.X && x < box.Right && y >= box.Y && y < box.Bottom)
            {
                return i;
            }
        }

        return null;
    }
}
