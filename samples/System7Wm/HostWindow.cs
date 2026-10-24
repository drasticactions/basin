using System7.Avalonia.Controls;
using System7.Avalonia.Rendering;
using System7.Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;
using Basin.Backend.Wayland;
using Basin.Capabilities;
using Basin.Scene;
using Basin.UI.Avalonia;
using Basin;
using Pixman;

namespace System7Wm;

internal sealed class HostWindow : HostFrameChrome, IUISurfaceObserver
{
    private const uint LeftButton = 0x110;

    private readonly Action _close;
    private readonly System7WindowFrame _window;
    private readonly LayoutTransformControl _zoom;
    private HostFrameInsets _insets;
    private readonly string _title;
    private System7ColorDepth _depth;
    private bool _forwarding;

    public HostWindow(WaylandHostFrame frame, AvaloniaUIHost host, string title, System7ColorDepth depth, Action close)
        : base(frame, host)
    {
        _title = title;
        _depth = depth;
        _close = close;
        _window = new System7WindowFrame
        {
            Kind = System7WindowKind.Document,
            Title = title,
            CanClose = true,
            CanZoom = true,
            CanResize = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _zoom = new LayoutTransformControl
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = _window,
        };
        _window.CloseRequested += (_, _) => _close();
        _window.ZoomRequested += (_, _) => Frame.SetMaximized(!Frame.Maximized);
        Relayout();
    }

    private AvaloniaUISurface? AvaloniaSurface => Surface as AvaloniaUISurface;

    public void SetDepth(System7ColorDepth depth)
    {
        _depth = depth;
        if ((Surface as AvaloniaUISurface)?.Content is Control root)
        {
            System7Theme.SetColorDepth(root, depth);
        }
    }

    public void OnSurfaceDamaged(IUISurface surface, PixmanRegion32 damage)
    {
        if (ReferenceEquals(surface, Surface))
        {
            Present();
        }
    }

    public void OnSurfaceDestroyed(IUISurface surface) => ForgetSurface(surface);

    protected override void OnSurfaceCreated(IUISurface surface)
    {
        surface.AddObserver(this);
        var root = new Panel { Background = null, Children = { _zoom } };
        root.Styles.Add(new Style(x => x.Is<Layoutable>())
        {
            Setters = { new Setter(Layoutable.UseLayoutRoundingProperty, false) },
        });
        System7Theme.SetColorDepth(root, _depth);
        ((AvaloniaUISurface)surface).AccessibleName = _window.Title;
        ((AvaloniaUISurface)surface).Content = root;
    }

    protected override void OnDisposingSurface(IUISurface surface) => surface.RemoveObserver(this);

    protected override void Relayout()
    {
        if (IsDisposed)
        {
            return;
        }

        var chrome = DevicePlacement.ChromeScale(Frame.Scale);
        var zoom = chrome / Frame.Scale;
        int Fit(int dip, int slack) => (int)Math.Ceiling(((dip * chrome) + slack) / Frame.Scale - 1e-9);
        var slack = chrome == Frame.Scale ? 0 : chrome - 1;
        var insets = Frame.Fullscreen ? default : new HostFrameInsets(Fit(ClientFrame.TitleHeight, 0), Fit(2, slack), Fit(2, slack), Fit(1, 0));
        _insets = insets;
        if (!Frame.SetInsets(insets) || insets.IsEmpty)
        {
            return;
        }

        _window.IsActive = Frame.Activated;
        var width = Frame.OuterWidth;
        var height = Frame.OuterHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        _zoom.LayoutTransform = new Avalonia.Media.ScaleTransform(zoom, zoom);
        _window.Width = Math.Floor(width / zoom);
        _window.Height = Math.Floor(height / zoom);
        _ = EnsureSurface();
    }

    private System7WindowPart PartAt(double x, double y)
    {
        var zoom = DevicePlacement.ChromeScale(Frame.Scale) / Frame.Scale;
        return System7WindowGeometry.HitTest(System7WindowKind.Document, (int)_window.Width, (int)_window.Height,
            (int)(x / zoom), (int)(y / zoom), Frame.Activated, true, true, false);
    }

    private HostFrameEdges EdgesAt(double x, double y)
    {
        var right = x >= Frame.OuterWidth - _insets.Right;
        var bottom = y >= Frame.OuterHeight - _insets.Bottom;
        var nearRight = x >= Frame.OuterWidth - 16;
        var nearBottom = y >= Frame.OuterHeight - 16;
        return (right && nearBottom) || (bottom && nearRight) ? HostFrameEdges.BottomRight
            : right ? HostFrameEdges.Right
            : bottom ? HostFrameEdges.Bottom
            : HostFrameEdges.None;
    }

    protected override void OnPointerEnter(double x, double y) => AvaloniaSurface?.NotifyPointerEnter(x, y);

    protected override void OnPointerMotion(uint timeMs, double x, double y) => AvaloniaSurface?.NotifyPointerMotion(timeMs, x, y);

    protected override void OnPointerLeave()
    {
        _forwarding = false;
        AvaloniaSurface?.NotifyPointerLeave();
    }

    protected override void OnPointerButton(uint timeMs, uint button, bool pressed)
    {
        if (button != LeftButton)
        {
            return;
        }

        if (!pressed)
        {
            if (_forwarding)
            {
                _forwarding = false;
                AvaloniaSurface?.NotifyPointerButton(timeMs, button, false);
            }

            return;
        }

        if (EdgesAt(PointerX, PointerY) is var edges and not HostFrameEdges.None)
        {
            Frame.StartResize(edges);
            return;
        }

        switch (PartAt(PointerX, PointerY))
        {
            case System7WindowPart.Close or System7WindowPart.Zoom:
                _forwarding = true;
                AvaloniaSurface?.NotifyPointerButton(timeMs, button, true);
                break;
            case System7WindowPart.TitleBar:
                Frame.StartMove();
                break;
        }
    }
}
