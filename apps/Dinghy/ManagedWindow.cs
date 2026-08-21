using Basin.WindowManager;

namespace Dinghy;

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

    public Rect? PreFullscreen { get; set; }

    public Rect? PreSnap { get; set; }

    public SnapState? Snap { get; set; }

    public bool Maximized { get; set; }

    public WmOutput? FullscreenOutput { get; set; }

    public bool Hidden { get; set; }

    public ulong MinimizeSeq { get; set; }

    public WindowCapabilities? SentCapabilities { get; set; }

    public WindowChrome Chrome { get; set; } = WindowChrome.ClientSide;

    public bool RuleForceSsd { get; set; }

    public int RuleSwallowTop { get; set; }

    public int SwallowTop => Chrome == WindowChrome.ServerSide ? Math.Max(RuleSwallowTop, 0) : 0;

    public WindowChrome? SentChrome { get; set; }

    public TitlebarSurface? Titlebar { get; set; }

    public ShadowSurface? Shadow { get; set; }

    public FrameStyle FrameStyle =>
        IsDialog ? FrameStyle.Dialog : IsFixedSize ? FrameStyle.FixedSize : FrameStyle.Normal;

    public TitlebarButton? TitlebarHovered { get; set; }

    public TitlebarButton? TitlebarPressed { get; set; }

    public bool TitlebarLeftDown { get; set; }

    public Rect ContentRect => new(X, Y, Width, Height);

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

    public void SetPosition(int x, int y)
    {
        Window.Node.SetPosition(x, y);
        X = x;
        Y = y;
        PositionUndefined = false;
        RefreshPreFullscreen();
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
        RefreshPreFullscreen();
    }

    public void Propose(int width, int height)
    {
        var minimum = Window.SizeHint.Minimum;
        Window.ProposeDimensions(Math.Max(width, minimum.Width), Math.Max(height, minimum.Height));
    }

    public void ProposePreferred() => Window.ProposeDimensions(0, 0);

    private void RefreshPreFullscreen()
    {
        if (FullscreenOutput is null && !PositionUndefined && Width > 0 && Height > 0)
        {
            PreFullscreen = ContentRect;
        }
    }
}
