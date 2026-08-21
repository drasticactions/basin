using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Basin.Capabilities;

namespace Basin.UI.Avalonia;

internal sealed class BasinScreens : IScreenImpl
{
    private readonly IUIScreenSource? _source;
    private readonly List<Screen> _screens = [];
    private bool _stale = true;

    public BasinScreens(IUIScreenSource? source)
    {
        _source = source;
        if (source is not null)
        {
            source.Changed += OnSourceChanged;
        }
    }

    public Action? Changed { get; set; }

    public int ScreenCount => Snapshot().Count;

    public IReadOnlyList<Screen> AllScreens => Snapshot();

    public Screen? ScreenFromWindow(IWindowBaseImpl window) => ScreenFromTopLevel(window);

    public Screen? ScreenFromTopLevel(ITopLevelImpl topLevel) => topLevel is BasinTopLevelImpl impl
        ? ScreenFromPoint(impl.ScreenPosition)
        : Snapshot().FirstOrDefault();

    public Screen? ScreenFromPoint(PixelPoint point) =>
        Snapshot().FirstOrDefault(s => s.Bounds.Contains(point)) ?? Snapshot().FirstOrDefault();

    public Screen? ScreenFromRect(PixelRect rect) =>
        Snapshot().FirstOrDefault(s => s.Bounds.Intersects(rect)) ?? Snapshot().FirstOrDefault();

    public Task<bool> RequestScreenDetails() => Task.FromResult(true);

    private void OnSourceChanged()
    {
        _stale = true;
        Changed?.Invoke();
    }

    private List<Screen> Snapshot()
    {
        if (!_stale)
        {
            return _screens;
        }

        _stale = false;
        _screens.Clear();
        if (_source is null)
        {
            _screens.Add(new BasinScreen(new UIScreenInfo("basin", 0, 0, 1920, 1080, 1.0, true)));
            return _screens;
        }

        for (var i = 0; i < _source.Count; i++)
        {
            if (_source.TryGet(i, out var info))
            {
                _screens.Add(new BasinScreen(info));
            }
        }

        if (_screens.Count == 0)
        {
            _screens.Add(new BasinScreen(new UIScreenInfo("basin", 0, 0, 1920, 1080, 1.0, true)));
        }

        return _screens;
    }

    private sealed class BasinScreen : PlatformScreen
    {
        public BasinScreen(in UIScreenInfo info)
            : base(new PlatformHandle(info.Name.GetHashCode(), "BasinOutput"))
        {
            DisplayName = info.Name;
            Scaling = info.Scale;
            Bounds = new PixelRect(info.X, info.Y, info.Width, info.Height);
            WorkingArea = Bounds;
            IsPrimary = info.IsPrimary;
        }
    }
}
