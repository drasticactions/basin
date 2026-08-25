using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.UI.Avalonia;
using PlasmaHost.Shell;

namespace PlasmaHost;

internal sealed class PlasmaDesktopOsd : IDisposable
{
    private const int Width = 260;
    private const int Height = 96;
    private const int HoldMillis = 900;

    private readonly PlasmaShellSurface _surface;
    private readonly DesktopOsdModel _model = new();
    private readonly BreezeTheme _theme;
    private readonly PlasmaHostDesktops _desktops;
    private readonly OutputLayout _layout;
    private readonly ICompositorEventLoop _loop;
    private IEventSource? _timer;
    private bool _disposed;

    public PlasmaDesktopOsd(
        AvaloniaUIHost host,
        UISurfaceIndex index,
        SceneTree layer,
        PlasmaHostDesktops desktops,
        OutputLayout layout,
        ICompositorEventLoop loop,
        BreezeTheme theme)
    {
        _surface = new PlasmaShellSurface(host, index, layer);
        _theme = theme;
        _model.Brushes = theme.Shell;
        _desktops = desktops;
        _layout = layout;
        _loop = loop;
    }

    public event Action? Repaint;

    public void RefreshTheme() => _model.Brushes = _theme.Shell;

    public void Announce()
    {
        if (_disposed)
        {
            return;
        }

        var index = _desktops.Current;
        _model.Count = _desktops.Desktops.Count;
        _model.Index = index;
        _model.Name = _desktops.Desktops[index].Name;

        var bounds = _layout.Bounds;
        var box = new Box(
            bounds.X + ((bounds.Width - Width) / 2),
            bounds.Y + ((bounds.Height - Height) / 2),
            Width,
            Height);
        _surface.Show(box, ScaleAt(box), () => new DesktopOsdView { DataContext = _model });

        _timer ??= _loop.AddTimer(Fade);
        _timer.UpdateTimer(HoldMillis);
        Repaint?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer?.Remove();
        _timer = null;
        _surface.Dispose();
    }

    private void Fade()
    {
        _surface.Hide();
        Repaint?.Invoke();
    }

    private double ScaleAt(in Box box) => _layout.OutputAt(box.X + 1, box.Y + 1)?.Scale ?? 1.0;
}
