using Avalonia.Controls;
using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.UI.Avalonia;

namespace PlasmaHost;

internal sealed class PlasmaShellSurface : IDisposable
{
    private readonly AvaloniaUIHost _host;
    private readonly UISurfaceIndex _index;
    private readonly SceneTree _layer;
    private AvaloniaUISurface? _surface;
    private UISurfaceNode? _node;
    private Box _box;
    private double _scale;
    private bool _disposed;

    public PlasmaShellSurface(AvaloniaUIHost host, UISurfaceIndex index, SceneTree layer)
    {
        _host = host;
        _index = index;
        _layer = layer;
    }

    public AvaloniaUISurface? Surface => _surface;

    public Box Box => _box;

    public bool Visible
    {
        get => _node is { } node && node.Node.Enabled;
        set
        {
            if (_node is { } node)
            {
                node.Node.Enabled = value;
            }
        }
    }

    public bool Owns(IUISurface surface) => _surface is { } mine && ReferenceEquals(mine, surface);

    public object? Show(in Box box, double scale, Func<Control> content)
    {
        if (_disposed || box.IsEmpty || scale <= 0)
        {
            return null;
        }

        if (_surface is null)
        {
            if (_host.CreateSurface(new UISurfaceOptions
            {
                Target = _host.Produces,
                Width = box.Width,
                Height = box.Height,
                Scale = scale,
            }) is not AvaloniaUISurface created)
            {
                return null;
            }

            created.Content = content();
            _surface = created;
            _node = new UISurfaceNode(_layer, created, _index) { PreciseDamage = true };
        }
        else if (box.Width != _box.Width || box.Height != _box.Height || scale != _scale)
        {
            _node!.Configure(box.Width, box.Height, scale);
        }

        _box = box;
        _scale = scale;
        _surface.SetPosition(box.X, box.Y);
        _node!.SetPosition(box.X, box.Y);
        _node.Node.Enabled = true;
        return _surface.Content;
    }

    public void Hide()
    {
        if (_node is { } node)
        {
            node.Node.Enabled = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _node?.Dispose();
        _surface?.Dispose();
        _node = null;
        _surface = null;
    }
}
