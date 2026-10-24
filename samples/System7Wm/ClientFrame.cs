using Avalonia.Controls;
using Avalonia.Layout;
using Basin;
using Basin.Scene;
using Basin.UI.Avalonia;
using System7.Avalonia;
using System7.Avalonia.Controls;
using System7.Avalonia.Rendering;

namespace System7Wm;

internal sealed class ClientFrame : IDisposable
{
    public const int TitleHeight = System7WindowFrame.StripHeight;

    private readonly System7WindowFrame _title;
    private readonly ChromeSurface _strip;
    private readonly ChromeSurface _grow;
    private readonly DeviceRect _left;
    private readonly DeviceRect _right;
    private readonly DeviceRect _bottom;
    private readonly DeviceRect _shadowRight;
    private readonly DeviceRect _shadowBottom;
    private Box _client;
    private int _originX;
    private int _originY;
    private double _scale = 1;

    public ClientFrame(SceneTree parent, AvaloniaUIHost host, UISurfaceIndex index)
    {
        _title = new System7WindowFrame
        {
            IsTitleStrip = true,
            Kind = System7WindowKind.Document,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _left = new DeviceRect(parent, 0xff000000);
        _right = new DeviceRect(parent, 0xff000000);
        _bottom = new DeviceRect(parent, 0xff000000);
        _shadowRight = new DeviceRect(parent, 0xff000000);
        _shadowBottom = new DeviceRect(parent, 0xff000000);
        _strip = new ChromeSurface(parent, host, index, () => new Panel { Children = { _title } }, "");
        _grow = new ChromeSurface(parent, host, index, () => new System7GrowBox(), null);
        _grow.Enabled = false;
        _title.CloseRequested += (_, _) => CloseRequested?.Invoke();
        _title.ZoomRequested += (_, _) => ZoomRequested?.Invoke();
    }

    public event Action? CloseRequested;

    public event Action? ZoomRequested;

    public ChromeSurface Strip => _strip;

    public ChromeSurface GrowBox => _grow;

    public System7WindowFrame Title => _title;

    public bool Shaded { get; set; }

    public int Chrome { get; private set; } = 1;

    public Box Client => _client;

    public Box FrameBox => new(_client.X - Chrome, _client.Y - (TitleHeight * Chrome), _client.Width + (3 * Chrome), (Shaded ? 0 : _client.Height) + ((TitleHeight + 2) * Chrome));

    public Box StripBox => new(_client.X - Chrome, _client.Y - (TitleHeight * Chrome), _client.Width + (2 * Chrome), TitleHeight * Chrome);

    public Box GrowBoxBox => new(_client.Right - (System7GrowBox.Size * Chrome), _client.Bottom - (System7GrowBox.Size * Chrome), System7GrowBox.Size * Chrome, System7GrowBox.Size * Chrome);

    public void SetDepth(System7ColorDepth depth)
    {
        System7Theme.SetColorDepth(_title, depth);
        if (_grow.Root is { } grow)
        {
            System7Theme.SetColorDepth(grow, depth);
        }
    }

    public void Update(int originX, int originY, double scale, in Box client, string title, bool active, bool canClose, bool canZoom, bool canResize)
    {
        _originX = originX;
        _originY = originY;
        _scale = scale;
        _client = client;
        Chrome = DevicePlacement.ChromeScale(scale);
        var n = Chrome;
        _title.Title = title;
        _strip.AccessibleName = title;
        _title.IsActive = active;
        _title.CanClose = canClose;
        _title.CanZoom = canZoom;
        _title.CanResize = canResize;
        _strip.Place(originX, originY, scale, StripBox);
        var height = Shaded ? 0 : client.Height;
        _left.Place(originX, originY, scale, new Box(client.X - n, client.Y - (TitleHeight * n), n, height + ((TitleHeight + 1) * n)));
        _right.Place(originX, originY, scale, new Box(client.Right, client.Y - (TitleHeight * n), n, height + ((TitleHeight + 1) * n)));
        _bottom.Place(originX, originY, scale, new Box(client.X - n, client.Y + height, client.Width + (2 * n), n));
        _shadowRight.Place(originX, originY, scale, new Box(client.Right + n, client.Y - (TitleHeight * n) + n, n, height + ((TitleHeight + 1) * n)));
        _shadowBottom.Place(originX, originY, scale, new Box(client.X, client.Y + height + n, client.Width + (2 * n), n));
        var showGrow = active && canResize && !Shaded && client.Width >= System7GrowBox.Size * n && client.Height >= System7GrowBox.Size * n;
        if (showGrow)
        {
            _grow.Place(originX, originY, scale, GrowBoxBox);
        }

        _grow.Enabled = showGrow;
        _grow.Node.AutoEnable = showGrow;
    }

    public void Raise()
    {
        foreach (var node in (SceneNode[])[_left.Node, _right.Node, _bottom.Node, _shadowRight.Node, _shadowBottom.Node, _strip.Node.Node, _grow.Node.Node])
        {
            node.RaiseToTop();
        }
    }

    public void Dispose()
    {
        _strip.Dispose();
        _grow.Dispose();
        _left.Dispose();
        _right.Dispose();
        _bottom.Dispose();
        _shadowRight.Dispose();
        _shadowBottom.Dispose();
    }
}
