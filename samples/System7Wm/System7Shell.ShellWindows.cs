using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Layout;
using Basin;
using Basin.Diagnostics;
using Basin.Scene;
using System7.Avalonia;
using System7.Avalonia.Controls;

namespace System7Wm;

internal sealed partial class System7Shell
{
    internal ShellWindow? ShellWindowByKey(string key) => _stack.FirstOrDefault(w => w.IsShell && w.ShellKey == key && w.Mapped);

    internal ShellWindow OpenShellWindow(string key, string title, Control content, int width, int height,
        bool resizable = false, bool closable = true, System7WindowKind kind = System7WindowKind.Document, bool alert = false,
        bool scrollBars = false, (int X, int Y)? placement = null, AppGroup? app = null)
    {
        if (ShellWindowByKey(key) is { } existing)
        {
            Activate(existing);
            return existing;
        }

        var container = new SceneTree(alert ? _layers.Alerts : _layers.Windows);
        var frame = new System7WindowFrame
        {
            Kind = kind,
            Title = title,
            CanClose = closable && kind == System7WindowKind.Document,
            CanZoom = resizable && kind == System7WindowKind.Document,
            CanResize = resizable && kind == System7WindowKind.Document,
            HasScrollBars = scrollBars,
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        System7Theme.SetColorDepth(frame, _config.Depth);
        var root = new Panel { Background = null, Children = { frame } };
        var surface = new ChromeSurface(container, _ui, _chrome, () => root, title);
        var group = app ?? _finder;
        var window = new ShellWindow(container, group)
        {
            ShellKey = key,
            ShellTitle = title,
            ShellResizable = resizable,
            ShellClosable = closable,
            IsAlert = alert,
            ShellSurface = surface,
            ShellFrame = frame,
            Mapped = true,
            ContentDip = (width, height),
            Owner = WindowOf,
        };
        window.ShellRoot = root;
        window.Closing = () => CloseShellWindow(window);
        root.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if ((e.Source as Avalonia.Visual)?.FindAncestorOfType<ComboBox>(includeSelf: true) is { IsDropDownOpen: false } && !window.ShellGrown)
            {
                GrowShellWindow(window, true);
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        content.AttachedToVisualTree += (_, _) =>
        {
            foreach (var box in content.GetLogicalDescendants().OfType<ComboBox>())
            {
                box.DropDownClosed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => GrowShellWindow(window, false), Avalonia.Threading.DispatcherPriority.Background);
            }
        };
        frame.CloseRequested += (_, _) => window.Close();
        frame.ZoomRequested += (_, _) => Zoom(window);

        var view = placement is { } at
            ? _outputs.Views.FirstOrDefault(v => _layout.BoxOf(v.Output) is var b && at.X >= b.X && at.Y >= b.Y && at.X < b.Right && at.Y < b.Bottom) ?? MainView ?? _outputs.Views[0]
            : MainView ?? _outputs.Views[0];
        var scale = view.Output.Scale;
        var n = DevicePlacement.ChromeScale(scale);
        var outputBox = _layout.BoxOf(view.Output);
        var area = WorkArea(view);
        var areaX = DevicePlacement.ToDevice(area.X - outputBox.X, scale);
        var areaY = DevicePlacement.ToDevice(area.Y - outputBox.Y, scale);
        var areaW = DevicePlacement.ToDeviceSpan(area.X - outputBox.X, area.Width, scale);
        var areaH = DevicePlacement.ToDeviceSpan(area.Y - outputBox.Y, area.Height, scale);
        if (alert)
        {
            window.ShellDevice = new Box(areaX + ((areaW - (width * n)) / 2), areaY + ((areaH - (height * n)) / 3), width * n, height * n);
        }
        else if (placement is { } place)
        {
            var x = Math.Clamp(DevicePlacement.ToDevice(place.X - outputBox.X, scale), areaX - (width * n) + (40 * n), areaX + areaW - (40 * n));
            var y = Math.Clamp(DevicePlacement.ToDevice(place.Y - outputBox.Y, scale), areaY + (ClientFrame.TitleHeight * n), areaY + areaH - (20 * n));
            window.ShellDevice = new Box(x, y, width * n, height * n);
        }
        else
        {
            var step = 20 * n * (_stagger % 6);
            _stagger = (_stagger + 1) % 8;
            window.ShellDevice = new Box(areaX + (40 * n) + step, areaY + (40 * n) + step, width * n, height * n);
        }

        SyncShellLogical(window, view);
        _stack.Add(window);
        if (!group.IsFinder)
        {
            _apps[group.Key] = group;
        }

        group.Windows.Add(window);
        UpdateShellWindow(window);
        Activate(window);
        BasinReport.Line($"SHELLWINDOW + {window.Id} {key}");
        return window;
    }

    private void GrowShellWindow(ShellWindow window, bool grow)
    {
        if (window.ShellGrown == grow || !window.Mapped)
        {
            return;
        }

        window.ShellGrown = grow;
        UpdateShellWindow(window);
        if (grow && window.ShellRoot is { } root && window.ShellSurface is { } surface)
        {
            root.Measure(new Size(surface.DipWidth, surface.DipHeight));
            root.Arrange(new Rect(0, 0, surface.DipWidth, surface.DipHeight));
        }

        _outputs.ScheduleAll();
    }

    internal void CloseShellWindow(ShellWindow window)
    {
        if (!window.Mapped)
        {
            return;
        }

        window.ShellClosed?.Invoke();
        Forget(window, destroyed: true);
        window.ShellSurface?.Dispose();
        if (!window.Container.IsDestroyed)
        {
            window.Container.Destroy();
        }

        BasinReport.Line($"SHELLWINDOW - {window.Id} {window.ShellKey}");
    }

    internal void RetitleShellWindow(ShellWindow window, string title)
    {
        window.ShellTitle = title;
        UpdateFrame(window);
    }
}
