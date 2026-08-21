using Basin.WindowManager;

namespace RetroWm;

internal sealed class ManagedWindow(WmWindow window) : IWmManagedWindow
{
    public WmWindow Window { get; } = window;

    public Queue<(WindowEventKind Kind, WmOutput? Output)> Events { get; } = new();

    public WmOutput? Output { get; set; }

    public int Workspace { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public bool Zoomed { get; set; }

    public bool Iconized { get; set; }

    public ulong MinimizeSeq { get; set; }

    public WmOutput? FullscreenOutput { get; set; }

    public WindowCapabilities? SentCapabilities { get; set; }

    public WindowChrome Chrome { get; set; } = WindowChrome.ClientSide;

    public WindowChrome? SentChrome { get; set; }

    public bool RuleForceSsd { get; set; }

    public int RuleSwallowTop { get; set; }

    public int SwallowTop =>
        Chrome == WindowChrome.ServerSide && !IsDialog ? Math.Max(RuleSwallowTop, 0) : 0;

    public FrameSurface? Frame { get; set; }

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

    public void SetFrame(Rect content)
    {
        X = content.X;
        Y = content.Y;
        Width = content.Width;
        Height = content.Height;
    }

    public void Propose(int width, int height)
    {
        var minimum = Window.SizeHint.Minimum;
        Window.ProposeDimensions(Math.Max(width, minimum.Width), Math.Max(height, minimum.Height));
    }
}
