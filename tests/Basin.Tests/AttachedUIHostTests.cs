using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.UI.Avalonia;
using Xunit;

namespace Basin.Tests;

public sealed class AttachedUIHostTests
{
    private sealed class OneScreen : IUIScreenSource
    {
        public int Count => 1;

        public bool TryGet(int index, out UIScreenInfo info)
        {
            info = new UIScreenInfo("shell", 0, 0, 800, 600, 1.0, true);
            return index == 0;
        }

        public event Action? Changed
        {
            add
            {
            }

            remove
            {
            }
        }
    }

    private static AvaloniaUIHost Attach(ThreadAffinity owner) =>
        BasinPlatform.Attach(new BasinPlatformOptions { Screens = new OneScreen(), CompositorAffinity = owner });

    private static void Pump(int rounds = 5)
    {
        for (var i = 0; i < rounds; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void An_attached_host_renders_a_surface_without_starting_a_platform()
    {
        Assert.False(BasinPlatform.IsStarted);
        var owner = ThreadAffinity.Capture();
        using var host = Attach(owner);
        Assert.True(host.IsAttached);
        Assert.Null(host.NextDueMillis);
        host.Pump();

        var damaged = new List<IUISurface>();
        host.SurfaceDamaged += damaged.Add;
        var surface = Assert.IsType<AvaloniaUISurface>(host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Memory,
            Width = 300,
            Height = 32,
            Scale = 1.0,
        }));
        surface.Content = new Border { Background = global::Avalonia.Media.Brushes.Red };
        Pump();

        Assert.NotEmpty(damaged);
        Assert.True(surface.PublishDamage());
        Assert.False(surface.PublishDamage());
        Assert.True(surface.TryAcquire(out var frame));
        Assert.NotNull(frame.Buffer);
        Assert.Equal(300, frame.Buffer!.Width);
        frame.Dispose();
        surface.Dispose();
    }

    [AvaloniaFact]
    public void A_menu_opened_on_an_attached_surface_becomes_a_popup_surface()
    {
        var owner = ThreadAffinity.Capture();
        using var host = Attach(owner);
        var popups = new List<IUISurface>();
        host.PopupAppeared += popups.Add;
        var dismissed = new List<IUISurface>();
        host.PopupDismissed += dismissed.Add;

        var surface = (AvaloniaUISurface)host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Memory,
            Width = 400,
            Height = 32,
            Scale = 1.0,
        })!;
        var item = new MenuItem { Header = "Applications" };
        item.Items.Add(new MenuItem { Header = "Terminal" });
        var menu = new Menu();
        menu.Items.Add(item);
        surface.Content = new DockPanel { Children = { menu } };
        Pump();

        item.Open();
        Pump();
        Assert.Single(popups);
        var popup = Assert.IsType<AvaloniaUISurface>(popups[0]);
        Assert.True(popup.Size.Width > 0 && popup.Size.Height > 0);
        Assert.True(popup.PositionY >= 0);
        Assert.Same(surface, popup.Parent);
        Assert.Same(surface, popup.Owner);
        Assert.Null(surface.Parent);

        item.Close();
        Pump();
        Assert.Single(dismissed);
        surface.Dispose();
    }

    [AvaloniaFact]
    public void Input_notified_off_thread_is_delivered_on_the_dispatcher_in_order()
    {
        var owner = ThreadAffinity.Capture();
        using var host = Attach(owner);
        var surface = (AvaloniaUISurface)host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Memory,
            Width = 200,
            Height = 40,
            Scale = 1.0,
        })!;
        var clicks = 0;
        var button = new Button { Width = 200, Height = 40 };
        button.Click += (_, _) => clicks++;
        surface.Content = button;
        Pump();

        var worker = new Thread(() =>
        {
            surface.NotifyPointerEnter(20, 20);
            surface.NotifyPointerMotion(1, 20, 20);
            surface.NotifyPointerButton(2, 0x110, true);
            surface.NotifyPointerButton(3, 0x110, false);
        });
        worker.Start();
        worker.Join();
        Pump();

        Assert.Equal(1, clicks);
        surface.Dispose();
    }
}
