using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.UI.Avalonia;
using Westonia.Shell;

namespace Westonia;

internal sealed class ShellFrame : IDisposable
{
    public const int Margin = FrameModel.Margin;

    public const int BorderWidth = FrameModel.BorderWidth;

    public const int TitlebarHeight = FrameModel.TitlebarHeight;

    public const int InsetX = Margin + BorderWidth;

    public const int InsetY = Margin + TitlebarHeight;

    private const int TopStrip = Margin + TitlebarHeight;

    private const int BottomStrip = Margin + BorderWidth;

    private const int SideStrip = Margin + BorderWidth;

    private readonly AvaloniaUIHost _host;
    private readonly UISurfaceIndex _index;
    private readonly ShellWindow _window;
    private readonly FrameModel _title = new();
    private readonly FrameEdgeModel[] _edges =
    [
        new(FrameEdge.Left),
        new(FrameEdge.Right),
        new(FrameEdge.Bottom),
    ];

    private readonly Strip[] _strips = new Strip[4];
    private int _width;
    private int _height;
    private double _scale = 1.0;
    private bool _disposed;

    public ShellFrame(AvaloniaUIHost host, SceneTree parent, ShellWindow window, UISurfaceIndex index)
    {
        _host = host;
        _index = index;
        Parent = parent;
        _window = window;
        _title.Title = window.Window.Title;
    }

    public SceneTree Parent { get; }

    public Box OuterBox => new(_window.X - InsetX, _window.Y - InsetY, _width, _height);

    public bool OwnsSurface(IUISurface surface)
    {
        foreach (var strip in _strips)
        {
            if (strip.Surface is { } candidate && ReferenceEquals(candidate, surface))
            {
                return true;
            }
        }

        return false;
    }

    public void SetActive(bool active)
    {
        _title.Active = active;
        foreach (var edge in _edges)
        {
            edge.Active = active;
        }
    }

    public void SetTitle(string title) => _title.Title = title;

    public bool Update(double scale)
    {
        if (_disposed)
        {
            return false;
        }

        var geometry = _window.Window.Xdg.EffectiveGeometry;
        var width = geometry.Width + (InsetX * 2);
        var height = geometry.Height + InsetY + BottomStrip;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var interior = height - TopStrip - BottomStrip;
        if (interior <= 0)
        {
            return false;
        }

        _width = width;
        _height = height;
        _scale = scale;

        Place(0, new Box(0, 0, width, TopStrip), scale, () => new FrameTitleView { DataContext = _title });
        Place(1, new Box(0, TopStrip, SideStrip, interior), scale, () => new FrameEdgeView { DataContext = _edges[0] });
        Place(
            2,
            new Box(width - SideStrip, TopStrip, SideStrip, interior),
            scale,
            () => new FrameEdgeView { DataContext = _edges[1] });
        Place(
            3,
            new Box(0, height - BottomStrip, width, BottomStrip),
            scale,
            () => new FrameEdgeView { DataContext = _edges[2] });
        return true;
    }

    public bool AcceptsInputAt(double x, double y)
    {
        var box = OuterBox;
        var localX = x - box.X;
        var localY = y - box.Y;
        if (localX < Margin || localY < Margin ||
            localX >= box.Width - Margin || localY >= box.Height - Margin)
        {
            return false;
        }

        var interiorX = Margin + BorderWidth;
        var interiorY = Margin + TitlebarHeight;
        return localX < interiorX ||
               localY < interiorY ||
               localX >= box.Width - Margin - BorderWidth ||
               localY >= box.Height - Margin - BorderWidth;
    }

    public bool HitsTitlebar(double x, double y)
    {
        var box = OuterBox;
        var localX = x - box.X;
        var localY = y - box.Y;
        return localX >= Margin && localX < box.Width - Margin &&
               localY >= Margin && localY < Margin + TitlebarHeight;
    }

    public bool HitsClose(double x, double y)
    {
        var box = OuterBox;
        var localX = x - box.X;
        var localY = y - box.Y;
        var right = box.Width - Margin - BorderWidth;
        return localY >= Margin + 4 && localY < Margin + 23 &&
               localX >= right - 19 && localX < right;
    }

    public ResizeEdges EdgeAt(double x, double y)
    {
        var box = OuterBox;
        var localX = x - box.X;
        var localY = y - box.Y;
        var edges = ResizeEdges.None;

        if (localX >= Margin && localX < Margin + BorderWidth)
        {
            edges |= ResizeEdges.Left;
        }
        else if (localX >= box.Width - Margin - BorderWidth && localX < box.Width - Margin)
        {
            edges |= ResizeEdges.Right;
        }

        if (localY >= Margin && localY < Margin + BorderWidth)
        {
            edges |= ResizeEdges.Top;
        }
        else if (localY >= box.Height - Margin - BorderWidth && localY < box.Height - Margin)
        {
            edges |= ResizeEdges.Bottom;
        }

        return edges;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        for (var i = 0; i < _strips.Length; i++)
        {
            _strips[i].Node?.Dispose();
            _strips[i].Surface?.Dispose();
            _strips[i] = default;
        }
    }

    private void Place(int slot, in Box local, double scale, Func<Avalonia.Controls.Control> content)
    {
        var strip = _strips[slot];
        if (strip.Surface is null)
        {
            var created = _host.CreateSurface(new UISurfaceOptions
            {
                Target = _host.Produces,
                Width = local.Width,
                Height = local.Height,
                Scale = scale,
            }) as AvaloniaUISurface;
            if (created is null)
            {
                return;
            }

            created.Content = content();
            strip = new Strip(created, new UISurfaceNode(Parent, created, _index) { PreciseDamage = true });
            strip.Node.Node.LowerToBottom();
            _strips[slot] = strip;
        }
        else if (local.Width != strip.Width || local.Height != strip.Height || scale != strip.Scale)
        {
            strip.Surface.Configure(local.Width, local.Height, scale);
        }

        if (strip.Surface is not { } surface)
        {
            return;
        }

        _strips[slot] = strip with { Width = local.Width, Height = local.Height, Scale = scale };

        var outer = OuterBox;
        surface.SetPosition(outer.X + local.X, outer.Y + local.Y);
        strip.Node.SetPosition(local.X - InsetX, local.Y - InsetY);

        var taken = new Box(Margin, Margin, _width - (Margin * 2), _height - (Margin * 2)).Intersect(local);
        strip.Node.Node.InputBox = new Box(taken.X - local.X, taken.Y - local.Y, taken.Width, taken.Height);
    }

    private readonly record struct Strip(
        AvaloniaUISurface? Surface,
        UISurfaceNode Node,
        int Width = 0,
        int Height = 0,
        double Scale = 0);
}
