using Basin.WindowManager;

namespace DeskbarWm;

internal sealed class ManagedWindow(WmWindow window) : IWmManagedWindow
{
    public WmWindow Window { get; } = window;

    public Queue<(WindowEventKind Kind, WmOutput? Output)> Events { get; } = new();

    public WmOutput? Output { get; set; }

    public int X { get; private set; }

    public int Y { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public bool PositionUndefined { get; private set; } = true;

    public Rect ContentRect => new(X, Y, Width, Height);

    public Rect? PreFullscreen { get; set; }

    public WmOutput? FullscreenOutput { get; set; }

    public bool Hidden { get; set; }

    public uint WorkspaceMask { get; set; }

    public float TabLocation { get; set; }

    public WindowCapabilities? SentCapabilities { get; set; }

    public bool Ssd { get; set; }

    public bool? SentSsd { get; set; }

    public TabSurface? Tab { get; set; }

    public TabMetrics Metrics { get; set; }

    public FramePart? TabPressed { get; set; }

    public bool TabLeftDown { get; set; }

    public ZoomState? Zoom { get; set; }

    public SatArea? Area { get; set; }

    public Size? ProposedCell { get; set; }

    public WindowFeel Feel =>
        Window.Parent is null ? WindowFeel.Normal
        : IsFixedSize ? WindowFeel.Modal
        : WindowFeel.Floating;

    public bool IsDialog => Window.Parent is not null;

    public bool IsFixedSize
    {
        get
        {
            var hint = Window.SizeHint;
            return hint.Minimum.Width > 0 && hint.Minimum.Width == hint.Maximum.Width
                && hint.Minimum.Height > 0 && hint.Minimum.Height == hint.Maximum.Height;
        }
    }

    public Rect FrameRect
    {
        get
        {
            if (!Ssd)
            {
                return ContentRect;
            }

            var m = Metrics;
            return new Rect(
                X - m.BorderWidth,
                Y - m.BorderWidth - m.TabHeight,
                m.FrameWidth,
                m.FrameHeight);
        }
    }

    public void SetPosition(int x, int y)
    {
        Window.Node.SetPosition(x, y);
        X = x;
        Y = y;
        PositionUndefined = false;
    }

    public void SyncDimensions()
    {
        var size = Window.Dimensions;
        if (size.Width == Width && size.Height == Height)
        {
            return;
        }

        Width = size.Width;
        Height = size.Height;
    }

    public void Propose(int width, int height)
    {
        var minimum = Window.SizeHint.Minimum;
        Window.ProposeDimensions(Math.Max(width, minimum.Width), Math.Max(height, minimum.Height));
    }

    public void ProposePreferred() => Window.ProposeDimensions(0, 0);
}
