using Basin;
using Basin.Host;
using Basin.Scene;

namespace EightWm;

internal sealed class ShellView
{
    public ShellView(OutputView driver, Scene scene)
    {
        Driver = driver;
        Root = new SceneTree(scene.Root);
        Background = new SceneTree(Root);
        BackgroundFrame = new SceneTransform(Background);
        Vacant = new SceneTree(Root);
        Apps = new SceneTree(Root);
        Rails = new SceneTree(Root);
        Preview = new SceneTree(Root);
        Dragging = new SceneTree(Root);
        SplashLayer = new SceneTree(Root);
        SplashFrame = new SceneTransform(SplashLayer);
        Dim = new SceneTree(Root);
        DimFrame = new SceneTransform(Dim);
        Transients = new SceneTree(Root);
        Chrome = new SceneTree(Root);
        TitleFrame = new SceneTransform(Chrome);
        SwitcherFrame = new SceneTransform(Chrome);
        CharmsPaneFrame = new SceneTransform(Chrome);
        CharmsClockFrame = new SceneTransform(Chrome);
        CharmsFrame = new SceneTransform(Chrome);
        Overlay = new SceneTree(Root);
    }

    public OutputView Driver { get; }

    public IOutput Output => Driver.Output;

    public OutputGlobal Global => Driver.Global;

    public SceneTree Root { get; }

    public SceneTree Background { get; }

    public SceneTransform BackgroundFrame { get; }

    public Tween StartMotion;

    public SceneTree Vacant { get; }

    public SceneRect? VacantFill { get; set; }

    public SceneTree Apps { get; }

    public SceneTree Rails { get; }

    public SceneTree Preview { get; }

    public SceneRect? PreviewFill { get; set; }

    public SceneTree Dragging { get; }

    public SceneTree SplashLayer { get; }

    public SceneTransform SplashFrame { get; }

    public StartScreen? Start { get; set; }

    public ChromeSurface? Splash { get; set; }

    public Tween SplashMotion;

    public string SplashTitle { get; set; } = string.Empty;

    public uint SplashColor { get; set; }

    public long SplashDeadlineMillis { get; set; }

    public SceneTree Dim { get; }

    public SceneTransform DimFrame { get; }

    public SceneTree Transients { get; }

    public SceneTree Chrome { get; }

    public SceneTransform CharmsFrame { get; }

    public SceneTransform CharmsClockFrame { get; }

    public SceneTransform CharmsPaneFrame { get; }

    public SceneTransform SwitcherFrame { get; }

    public SwitcherRail? Switcher { get; set; }

    public Tween SwitcherMotion;

    public bool SwitcherDocked { get; set; }

    public SceneTransform TitleFrame { get; }

    public AppTitleBar? Title { get; set; }

    public CharmsBar? Charms { get; set; }

    public SceneRect? DimRect { get; set; }

    public SceneTree Overlay { get; }

    public SceneOutput? SceneOutput => Driver.Scene;

    public OutputScheduler? Scheduler => Driver.Scheduler;

    public long Rendered => Driver.Rendered;

    public int Width => Driver.Width;

    public int Height => Driver.Height;

    public double Scale => Driver.Scale;

    public Box Box => Driver.Box;

    public bool StartVisible { get; set; }

    public Box UsableArea { get; set; }

    public AppHost<AppWindow> Host { get; } = new();

    public List<SceneRect> Splitters { get; } = [];

    public int DraggingSplitter { get; set; } = -1;

    public bool IsPortrait => Box.Height > Box.Width;

    public void Reposition() => Root.SetPosition(Driver.Box.X, Driver.Box.Y);

    public void ReleaseChrome()
    {
        Switcher?.Dispose();
        Switcher = null;
        Title?.Dispose();
        Title = null;
        Charms?.Dispose();
        Charms = null;
        Start?.Dispose();
        Start = null;
        Splash?.Dispose();
        Splash = null;
    }

    public void Destroy()
    {
        ReleaseChrome();
        if (!Root.IsDestroyed)
        {
            Root.Destroy();
        }
    }
}
