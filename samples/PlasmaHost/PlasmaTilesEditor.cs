using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.UI.Avalonia;
using PlasmaHost.Shell;
using Xkb;

namespace PlasmaHost;

internal sealed class PlasmaTilesEditor : IDisposable
{
    private const int HandleGrab = 12;

    private readonly TilesEditorModel _model = new();
    private readonly PlasmaShellSurface _surface;
    private readonly BreezeTheme _theme;
    private readonly SceneTree _root;
    private readonly OutputLayout _layout;
    private readonly List<PlasmaTile> _leaves = [];
    private PlasmaTile _tiles = PlasmaTile.Default();
    private (PlasmaTile Parent, int Index)? _drag;
    private string _screen = "screen-0";
    private bool _open;
    private bool _disposed;

    public PlasmaTilesEditor(
        AvaloniaUIHost host, UISurfaceIndex index, SceneTree layer, OutputLayout layout, BreezeTheme theme)
    {
        _root = new SceneTree(layer) { Enabled = false };
        _surface = new PlasmaShellSurface(host, index, _root);
        _layout = layout;
        _theme = theme;
        _model.Brushes = theme.Shell;
    }

    public event Action? Repaint;

    public void RefreshTheme() => _model.Brushes = _theme.Shell;

    public bool IsOpen => _open;

    public void Toggle()
    {
        if (_open)
        {
            Close();
            return;
        }

        _screen = ScreenName();
        _tiles = KwinTiling.Load(_screen);
        _open = true;
        Rebuild();
    }

    public void Close()
    {
        if (!_open)
        {
            return;
        }

        _open = false;
        _drag = null;
        _root.Enabled = false;
        KwinTiling.Save(_screen, _tiles);
        Repaint?.Invoke();
    }

    public bool Key(XkbKeysym symbol)
    {
        if (!_open)
        {
            return false;
        }

        if (symbol.Name == "Escape")
        {
            Close();
        }

        return true;
    }

    public bool PointerMotion(double x, double y)
    {
        if (!_open)
        {
            return false;
        }

        if (_drag is { } drag)
        {
            Slide(drag.Parent, drag.Index, x, y);
            Rebuild();
            return true;
        }

        var over = HandleAt(x, y) is not null;
        foreach (var tile in _model.Tiles)
        {
            tile.Hot = over;
        }

        Repaint?.Invoke();
        return true;
    }

    public bool PointerButton(double x, double y, uint button, bool pressed)
    {
        if (!_open)
        {
            return false;
        }

        if (button == InputCodes.BtnRight && pressed)
        {
            Merge(x, y);
            return true;
        }

        if (button == InputCodes.BtnMiddle && pressed)
        {
            Split(x, y);
            return true;
        }

        if (button != InputCodes.BtnLeft)
        {
            return true;
        }

        if (!pressed)
        {
            _drag = null;
            return true;
        }

        _drag = HandleAt(x, y);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _surface.Dispose();
        _root.Destroy();
    }

    private void Rebuild()
    {
        var bounds = _layout.Bounds;
        var scale = _layout.OutputAt(bounds.X + 1, bounds.Y + 1)?.Scale ?? 1.0;
        _tiles.Place(bounds);
        _leaves.Clear();
        _tiles.Leaves(_leaves);

        _surface.Show(bounds, scale, () => new TilesEditorView { DataContext = _model });
        _root.Enabled = true;

        _model.Tiles.Clear();
        foreach (var leaf in _leaves)
        {
            _model.Tiles.Add(new TileBoxModel
            {
                X = leaf.Box.X - bounds.X + 6,
                Y = leaf.Box.Y - bounds.Y + 6,
                Width = Math.Max(1, leaf.Box.Width - 12),
                Height = Math.Max(1, leaf.Box.Height - 12),
            });
        }

        Repaint?.Invoke();
    }

    private void Split(double x, double y)
    {
        if (LeafAt(x, y) is not { } leaf)
        {
            return;
        }

        leaf.Split(leaf.Box.Width >= leaf.Box.Height);
        Rebuild();
    }

    private void Merge(double x, double y)
    {
        if (LeafAt(x, y) is not { } leaf || ParentOf(_tiles, leaf) is not { } parent)
        {
            return;
        }

        parent.Children.Clear();
        Rebuild();
    }

    private void Slide(PlasmaTile parent, int index, double x, double y)
    {
        var first = parent.Children[index];
        var second = parent.Children[index + 1];
        var total = first.Fraction + second.Fraction;
        var area = parent.Box;
        var position = parent.Horizontal
            ? (x - first.Box.X) / Math.Max(1, area.Width)
            : (y - first.Box.Y) / Math.Max(1, area.Height);
        var share = Math.Clamp(position, 0.1, total - 0.1);
        first.Fraction = share;
        second.Fraction = total - share;
    }

    private (PlasmaTile Parent, int Index)? HandleAt(double x, double y) => HandleIn(_tiles, x, y);

    private static (PlasmaTile Parent, int Index)? HandleIn(PlasmaTile tile, double x, double y)
    {
        for (var i = 0; i < tile.Children.Count - 1; i++)
        {
            var first = tile.Children[i].Box;
            if (tile.Horizontal)
            {
                if (Math.Abs(x - first.Right) <= HandleGrab && y >= first.Y && y < first.Bottom)
                {
                    return (tile, i);
                }
            }
            else if (Math.Abs(y - first.Bottom) <= HandleGrab && x >= first.X && x < first.Right)
            {
                return (tile, i);
            }
        }

        foreach (var child in tile.Children)
        {
            if (HandleIn(child, x, y) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private PlasmaTile? LeafAt(double x, double y)
    {
        foreach (var leaf in _leaves)
        {
            var box = leaf.Box;
            if (x >= box.X && x < box.Right && y >= box.Y && y < box.Bottom)
            {
                return leaf;
            }
        }

        return null;
    }

    private static PlasmaTile? ParentOf(PlasmaTile tile, PlasmaTile child)
    {
        foreach (var candidate in tile.Children)
        {
            if (ReferenceEquals(candidate, child))
            {
                return tile;
            }

            if (ParentOf(candidate, child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private string ScreenName()
    {
        foreach (var (output, _) in _layout.Outputs)
        {
            return output.Name;
        }

        return "screen-0";
    }
}
