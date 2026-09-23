using Basin;
using Basin.Capabilities;
using Basin.Scene;

namespace EightWm;

internal sealed class CharmsBar : IDisposable
{
    public const int BarWidth = 88;
    public const int ClockWidth = 320;
    public const int ClockHeight = 190;
    public const int ClockMargin = 40;
    public const int PaneWidth = 345;
    public const int CharmCount = 5;
    public const int CharmSpacing = 96;

    private static readonly string[] PaneText =
    [
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
    ];

    private readonly AvaloniaChrome _bar;
    private readonly AvaloniaChrome _clock;
    private readonly AvaloniaChrome _pane;
    private readonly CharmsBarView _barView;
    private readonly CharmPaneView _paneView;
    private readonly Pixman.PixmanRegion32 _backdropRegion = new();

    private int _width;
    private int _height;
    private double _scale = 1;

    public CharmsBar(
        IUIHost host,
        UISurfaceIndex index,
        SceneTransform barFrame,
        SceneTransform clockFrame,
        SceneTransform paneFrame,
        Action<Charm> activate,
        Action paneClosed)
    {
        BarFrame = barFrame;
        ClockFrame = clockFrame;
        PaneFrame = paneFrame;
        Model = new CharmsModel(name => activate(Enum.TryParse<Charm>(name, out var charm) ? charm : Charm.None));
        _barView = new CharmsBarView { DataContext = Model };
        _paneView = new CharmPaneView { DataContext = Pane };
        _paneView.Flyout.Closed += (_, _) => paneClosed();
        _bar = new AvaloniaChrome(barFrame, host, index, _barView) { Enabled = false };
        _clock = new AvaloniaChrome(clockFrame, host, index, new ClockView { DataContext = Model })
        {
            Enabled = false,
            InputEnabled = false,
        };
        _pane = new AvaloniaChrome(paneFrame, host, index, _paneView) { Enabled = false };
    }

    public SceneTransform BarFrame { get; }

    public SceneTransform ClockFrame { get; }

    public SceneTransform PaneFrame { get; }

    public CharmsModel Model { get; }

    public CharmPaneModel Pane { get; } = new();

    public CharmsBarView BarView => _barView;

    public CharmPaneView PaneView => _paneView;

    public Tween BarMotion;

    public Tween ClockMotion;

    public Tween PaneMotion;

    public bool Visible { get; private set; }

    public Charm OpenPane { get; private set; } = Charm.None;

    public Charm Hot => Enum.TryParse<Charm>(_barView.Hovered, out var charm) && Visible ? charm : Charm.None;

    public bool ClosingPane { get; set; }

    public string Clock
    {
        get => Model.Clock;
        set => Model.Clock = value;
    }

    public string Date
    {
        get => Model.Date;
        set => Model.Date = value;
    }

    public IBackdropEffect? Backdrop { get; set; }

    public SceneBuffer BarNode => _bar.Node;

    public SceneBuffer PaneNode => _pane.Node;

    public IUISurface? PaneSurface => _pane.Surface;

    public void Resize(int width, int height, double scale)
    {
        _width = width;
        _height = height;
        _scale = scale;
    }

    public Box BarBox => new(_width - BarWidth, 0, BarWidth, _height);

    public Box PaneBox => new(_width - PaneWidth, 0, PaneWidth, _height);

    public Box ClockBox => new(ClockMargin, _height - ClockHeight - ClockMargin, ClockWidth, ClockHeight);

    public void Show(bool visible)
    {
        Visible = visible;
        if (!visible)
        {
            return;
        }

        _bar.Enabled = true;
        _clock.Enabled = true;
    }

    public void Retire()
    {
        _bar.Enabled = false;
        _clock.Enabled = false;
    }

    public void RetirePane()
    {
        ClosingPane = false;
        _pane.Enabled = false;
        OpenPane = Charm.None;
        _paneView.Flyout.Hide();
    }

    public bool IsRetired => !_bar.Enabled;

    public bool ClockShown => _clock.Enabled;

    public bool PaneShown => _pane.Enabled;

    public bool AnyVisible => Visible || OpenPane != Charm.None;

    public void ShowPane(Charm charm)
    {
        OpenPane = charm;
        _pane.Enabled = charm != Charm.None;
        if (charm == Charm.None)
        {
            return;
        }

        Pane.Title = charm.ToString();
        Pane.Text = PaneText[(int)charm];
        Pane.IsSettings = charm == Charm.Settings;
        _paneView.Flyout.Show();
    }

    public bool Draw()
    {
        if (!AnyVisible || _width <= 0 || _height <= 0)
        {
            return false;
        }

        Model.FillAlpha = Backdrop is null ? (byte)0xf0 : (byte)0xc0;
        if (Visible)
        {
            if (_bar.Place(BarBox, _scale))
            {
                ApplyBackdrop(_bar);
            }

            _clock.Place(ClockBox, _scale);
        }

        if (OpenPane != Charm.None)
        {
            _pane.Place(PaneBox, _scale);
        }

        return true;
    }

    private void ApplyBackdrop(AvaloniaChrome surface)
    {
        if (Backdrop is not { } backdrop)
        {
            return;
        }

        var node = surface.Node;
        _backdropRegion.Clear();
        _backdropRegion.UnionRect(_backdropRegion, 0, 0, (uint)surface.Width, (uint)surface.Height);
        node.SetBackdropEffect(backdrop, _backdropRegion, node);
    }

    public void Dispose()
    {
        _backdropRegion.Dispose();
        _pane.Dispose();
        _clock.Dispose();
        _bar.Dispose();
    }
}
