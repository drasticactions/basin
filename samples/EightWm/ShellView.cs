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
        StartFrame = new SceneTransform(BackgroundFrame);
        AppsFrame = new SceneTransform(BackgroundFrame) { Enabled = false };
        Vacant = new SceneTree(Root);
        Apps = new SceneTree(Root);
        Rails = new SceneTree(Root);
        Preview = new SceneTree(Root);
        Dragging = new SceneTree(Root);
        FlipLayer = new SceneTree(Root);
        FlipFrame = new SceneTransform(FlipLayer) { Enabled = false };
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

    public SceneTransform StartFrame { get; }

    public SceneTransform AppsFrame { get; }

    public Tween StartPageMotion;

    public Tween AppsMotion;

    public StartModel StartModel { get; } = new();

    public AvaloniaChrome? Start { get; set; }

    public AvaloniaChrome? AppsSurface { get; set; }

    public StartView? StartView { get; set; }

    public AppsView? AppsView { get; set; }

    public bool AppsVisible { get; set; }

    public double IconScale { get; set; }

    public SceneTree Vacant { get; }

    public SceneRect? VacantFill { get; set; }

    public SceneTree Apps { get; }

    public SceneTree Rails { get; }

    public SceneTree Preview { get; }

    public SceneRect? PreviewFill { get; set; }

    public SceneTree Dragging { get; }

    public SceneTree FlipLayer { get; }

    public SceneTransform FlipFrame { get; }

    public AvaloniaChrome? FlipFace { get; set; }

    public FlipModel FlipModel { get; } = new();

    public LaunchFlip? Flip { get; set; }

    public SceneTransform? RecededPage { get; set; }

    public SceneRect? BackgroundFill { get; set; }

    public SceneTree SplashLayer { get; }

    public SceneTransform SplashFrame { get; }

    public AvaloniaChrome? Splash { get; set; }

    public SplashModel SplashModel { get; } = new();

    public Tween SplashMotion;

    public long SplashDeadlineMillis { get; set; }

    public Box SplashBox { get; set; }

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

    public int SplitPosition { get; set; }

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
        AppsSurface?.Dispose();
        AppsSurface = null;
        Splash?.Dispose();
        Splash = null;
        FlipFace?.Dispose();
        FlipFace = null;
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
