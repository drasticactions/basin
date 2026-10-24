using Avalonia;
using Avalonia.Controls;
using Basin;
using Basin.Scene;
using Basin.UI.Avalonia;
using System7.Avalonia.Rendering;

namespace System7Wm;

internal sealed class ChromeSurface : IDisposable
{
    private readonly UISurfaceNode _node;
    private readonly Func<Control> _content;
    private Control? _root;
    private string? _name;
    private bool _disposed;

    public ChromeSurface(SceneTree parent, AvaloniaUIHost host, UISurfaceIndex index, Func<Control> content, string? name)
    {
        _node = new UISurfaceNode(parent, host, index) { PreciseDamage = false };
        _content = content;
        _name = name;
    }

    public string? AccessibleName
    {
        get => _name;
        set
        {
            _name = value;
            if (Surface is { } surface)
            {
                surface.AccessibleName = value;
            }
        }
    }

    public UISurfaceNode Node => _node;

    public AvaloniaUISurface? Surface => _node.Surface as AvaloniaUISurface;

    public Control? Root => _root;

    public int ChromeScale { get; private set; } = 1;

    public Box Device { get; private set; }

    public int DipWidth { get; private set; }

    public int DipHeight { get; private set; }

    public bool Enabled
    {
        get => _node.Enabled;
        set => _node.Enabled = value;
    }

    public bool Place(int originX, int originY, double scale, in Box device, int? dipWidth = null, int? dipHeight = null)
    {
        if (_disposed || device.Width <= 0 || device.Height <= 0)
        {
            return false;
        }

        var chrome = _node.PlaceDevice(originX, originY, scale, device);
        var width = dipWidth ?? (int)Math.Ceiling(device.Width / (double)chrome);
        var height = dipHeight ?? (int)Math.Ceiling(device.Height / (double)chrome);
        if (!_node.Configure(width, height, chrome))
        {
            return false;
        }

        ChromeScale = chrome;
        Device = device;
        DipWidth = width;
        DipHeight = height;
        if (Surface is { } surface)
        {
            surface.SetPosition(device.X / (double)chrome, device.Y / (double)chrome);
            if (_root is null)
            {
                surface.Accessible = _name is not null;
                surface.AccessibleName = _name;
                _root = _content();
                surface.Content = _root;
            }

            System7Pattern.SetScreenOffset(_root, new PixelPoint(device.X / chrome, device.Y / chrome));
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
        _node.Dispose();
    }
}
