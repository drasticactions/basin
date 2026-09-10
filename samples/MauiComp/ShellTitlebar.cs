using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.UI.Avalonia;
using MauiComp.Shell;

namespace MauiComp;

internal sealed class ShellTitlebar : IDisposable
{
    public const int Height = 25;

    public const int EdgeWidth = 4;

    private static readonly RenderColor ActiveEdge = new(0x5B / 255f, 0x2A / 255f, 0xAB / 255f, 1f);

    private static readonly RenderColor InactiveEdge = new(0x74 / 255f, 0x4D / 255f, 0xC3 / 255f, 1f);

    private readonly AvaloniaUIHost _host;
    private readonly MauiSurfaces _surfaces;
    private readonly UISurfaceIndex _index;
    private readonly ShellWindow _window;
    private readonly TitlebarModel _model;
    private AvaloniaUISurface? _surface;
    private UISurfaceNode? _node;
    private readonly SceneRect[] _edges = new SceneRect[3];
    private bool _framed = true;
    private bool _active;
    private MauiScope? _scope;
    private int _width;
    private double _scale;
    private bool _disposed;

    public ShellTitlebar(
        AvaloniaUIHost host,
        MauiSurfaces surfaces,
        UISurfaceIndex index,
        ShellWindow window,
        Action close,
        Action maximize,
        Action minimize)
    {
        _host = host;
        _surfaces = surfaces;
        _index = index;
        _window = window;
        _model = new TitlebarModel(close, maximize, minimize) { Title = window.Label };
    }

    public IUISurface? Surface => _surface;

    public Box OuterBox => new(_window.Geometry.X - Inset, _window.Geometry.Y - Height, _width, Height);

    public bool Framed
    {
        get => _framed;
        set => _framed = value;
    }

    private int Inset => _framed ? EdgeWidth : 0;

    public void SetActive(bool active)
    {
        _active = active;
        _model.Active = active;
        foreach (var edge in _edges)
        {
            if (edge is { IsDestroyed: false })
            {
                edge.Color = active ? ActiveEdge : InactiveEdge;
            }
        }
    }

    public ResizeEdges EdgeAt(double x, double y)
    {
        if (!_framed || !Visible)
        {
            return ResizeEdges.None;
        }

        var geometry = _window.Geometry;
        var left = geometry.X - EdgeWidth;
        var right = geometry.Right + EdgeWidth;
        var top = geometry.Y - Height;
        var bottom = geometry.Bottom + EdgeWidth;
        if (x < left || x >= right || y < top || y >= bottom)
        {
            return ResizeEdges.None;
        }

        var edges = ResizeEdges.None;
        if (x < geometry.X)
        {
            edges |= ResizeEdges.Left;
        }
        else if (x >= geometry.Right)
        {
            edges |= ResizeEdges.Right;
        }

        if (y >= geometry.Bottom)
        {
            edges |= ResizeEdges.Bottom;
        }
        else if (y < top + EdgeWidth && edges != ResizeEdges.None)
        {
            edges |= ResizeEdges.Top;
        }

        return edges;
    }

    public static string? CursorFor(ResizeEdges edges) => edges switch
    {
        ResizeEdges.Left or ResizeEdges.Right => "ew-resize",
        ResizeEdges.Top or ResizeEdges.Bottom => "ns-resize",
        ResizeEdges.Top | ResizeEdges.Left or ResizeEdges.Bottom | ResizeEdges.Right => "nwse-resize",
        ResizeEdges.Top | ResizeEdges.Right or ResizeEdges.Bottom | ResizeEdges.Left => "nesw-resize",
        _ => null,
    };

    public bool Visible
    {
        get => _node?.Enabled ?? false;
        set
        {
            if (_node is not null)
            {
                _node.Enabled = value;
            }

            PlaceEdges();
        }
    }

    public void SetTitle(string title) => _model.Title = title;

    public bool OwnsSurface(IUISurface surface) => _surface is not null && ReferenceEquals(_surface, surface);

    public bool IsDragHandleAt(double x, double y)
    {
        var outer = OuterBox;
        return x >= outer.X && y >= outer.Y && y < outer.Bottom && x < outer.Right - ButtonStrip;
    }

    private const int ButtonStrip = 4 + (3 * 21) + (2 * 3) + 4;

    public bool Update(double scale)
    {
        if (_disposed)
        {
            return false;
        }

        var geometry = _window.Window.Xdg.EffectiveGeometry;
        if (geometry.Width <= 0)
        {
            return false;
        }

        var width = geometry.Width + (2 * Inset);
        if (_surface is null)
        {
            var created = _host.CreateSurface(new UISurfaceOptions
            {
                Target = _host.Produces,
                Width = width,
                Height = Height,
                Scale = scale,
            }) as AvaloniaUISurface;
            if (created is null)
            {
                return false;
            }

            _surface = created;
            _scope = _surfaces.Attach(created, new TitlebarPage { BindingContext = _model });
            _node = new UISurfaceNode(_window.Tree, created, _index) { PreciseDamage = true };
            _node.Node.LowerToBottom();
            for (var i = 0; i < _edges.Length; i++)
            {
                _edges[i] = new SceneRect(_window.Tree, 1, 1, _active ? ActiveEdge : InactiveEdge);
                _edges[i].LowerToBottom();
            }
        }
        else if (width != _width || scale != _scale)
        {
            _surface.Configure(width, Height, scale);
        }

        _width = width;
        _scale = scale;
        var outer = OuterBox;
        _surface.SetPosition(outer.X, outer.Y);
        _node!.SetPosition(geometry.X - Inset, geometry.Y - Height);
        PlaceEdges();
        return true;
    }

    private void PlaceEdges()
    {
        if (_edges[0] is null)
        {
            return;
        }

        var geometry = _window.Window.Xdg.EffectiveGeometry;
        var shown = _framed && Visible;
        var height = geometry.Height + EdgeWidth;
        Place(_edges[0], geometry.X - EdgeWidth, geometry.Y, EdgeWidth, height, shown);
        Place(_edges[1], geometry.Right, geometry.Y, EdgeWidth, height, shown);
        Place(_edges[2], geometry.X, geometry.Bottom, geometry.Width, EdgeWidth, shown);
    }

    private static void Place(SceneRect rect, int x, int y, int width, int height, bool shown)
    {
        if (rect.IsDestroyed)
        {
            return;
        }

        rect.Enabled = shown;
        rect.Width = Math.Max(1, width);
        rect.Height = Math.Max(1, height);
        rect.SetPosition(x, y);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var edge in _edges)
        {
            if (edge is { IsDestroyed: false })
            {
                edge.Destroy();
            }
        }

        _node?.Dispose();
        _node = null;
        _scope?.Dispose();
        _scope = null;
        _surface?.Dispose();
        _surface = null;
    }
}
