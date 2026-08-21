using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Basin.Avalonia;
using Basin.Diagnostics;
using Basin.Shell.Xdg.Protocol;
using Basin.Tests;
using Xunit;

namespace Waylonia.Tests;

public sealed class HostWindowTests
{
    [AvaloniaFact]
    public void A_mapped_toplevel_becomes_one_host_window()
    {
        using var harness = new WayloniaHostHarness();
        var toplevel = harness.MapToplevel(width: 120, height: 90, title: "notes", appId: "org.basin.notes");

        var window = Assert.Single(harness.Windows.Windows);
        Assert.Equal("notes", window.Title);
        Assert.True(window.IsVisible);

        toplevel.Destroy();
        harness.PumpUntil(() => harness.Windows.Windows.Count == 0, "the host window outlived its toplevel");
    }

    [AvaloniaFact]
    public void A_title_change_reaches_the_host_window()
    {
        using var harness = new WayloniaHostHarness();
        var toplevel = harness.MapToplevel(title: "before");
        var window = Assert.Single(harness.Windows.Windows);
        Assert.Equal("before", window.Title);

        toplevel.Toplevel.SetTitle("after");
        toplevel.Surface.Commit();
        harness.PumpUntil(() => window.Title == "after", "the host window kept the old title");

        toplevel.Destroy();
        harness.PumpUntil(() => harness.Windows.Windows.Count == 0, "the host window outlived its toplevel");
    }

    [AvaloniaFact]
    public void Two_toplevels_become_two_host_windows_and_a_count_change_each()
    {
        using var harness = new WayloniaHostHarness();
        var counts = new List<int>();
        harness.Windows.CountChanged += counts.Add;

        var first = harness.MapToplevel(title: "first");
        var second = harness.MapToplevel(title: "second");

        Assert.Equal(2, harness.Windows.Windows.Count);
        Assert.Equal([1, 2], counts);

        second.Destroy();
        harness.PumpUntil(() => harness.Windows.Windows.Count == 1, "the second host window stayed open");
        first.Destroy();
        harness.PumpUntil(() => harness.Windows.Windows.Count == 0, "the first host window stayed open");
    }

    [AvaloniaFact]
    public void Closing_the_host_window_asks_the_client_to_close_rather_than_killing_it()
    {
        using var harness = new WayloniaHostHarness();
        var toplevel = harness.MapToplevel();
        var window = Assert.Single(harness.Windows.Windows);

        window.Close();
        harness.PumpUntil(() => toplevel.CloseReceived, "the client was never asked to close");

        Assert.Single(harness.Windows.Windows);
        Assert.False(toplevel.Surface.IsDestroyed);

        toplevel.Destroy();
        harness.PumpUntil(() => harness.Windows.Windows.Count == 0, "the host window outlived its toplevel");
    }

    [AvaloniaFact]
    public void A_host_resize_configures_the_client()
    {
        using var harness = new WayloniaHostHarness();
        var toplevel = harness.MapToplevel(width: 120, height: 90);
        var window = Assert.Single(harness.Windows.Windows);

        var before = (toplevel.ConfiguredWidth, toplevel.ConfiguredHeight);
        window.Width = 200;
        window.Height = 150;
        Dispatcher.UIThread.RunJobs();
        harness.PumpUntil(
            () => (toplevel.ConfiguredWidth, toplevel.ConfiguredHeight) != before,
            "the host resize never reached the client as a configure");

        Assert.True(toplevel.ConfiguredWidth >= 120, $"the configure narrowed to {toplevel.ConfiguredWidth}");
        Assert.True(toplevel.ConfiguredHeight >= 90, $"the configure shortened to {toplevel.ConfiguredHeight}");

        toplevel.Destroy();
        harness.PumpUntil(() => harness.Windows.Windows.Count == 0, "the host window outlived its toplevel");
    }

    [AvaloniaFact]
    public void A_client_that_maps_and_goes_leaves_nothing_tracked_behind()
    {
        LeakTracking.Require();
        using (var harness = new WayloniaHostHarness())
        {
            var toplevel = harness.MapToplevel();
            Assert.Single(harness.Windows.Windows);
            toplevel.Destroy();
            harness.PumpUntil(() => harness.Windows.Windows.Count == 0, "the host window outlived its toplevel");
        }

        Dispatcher.UIThread.RunJobs();
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
        LeakTracking.Expect(0, BasinCounters.PendingFrees);
    }

    [AvaloniaFact]
    public void A_layer_surface_that_unmaps_and_maps_again_gets_its_host_window_back()
    {
        using var harness = new WayloniaHostHarness();
        var panel = harness.MapLayer(width: 200, height: 40);

        var window = Assert.Single(harness.Windows.LayerWindows);
        Assert.True(window.IsVisible);

        harness.HideLayer(panel);
        harness.PumpUntil(() => !window.IsVisible, "the host window outlived the unmapped layer surface");
        Assert.Single(harness.Windows.LayerWindows);

        harness.ShowLayer(panel);
        harness.PumpUntil(() => window.IsVisible, "the remapped layer surface never came back on screen");
        Assert.Same(window, Assert.Single(harness.Windows.LayerWindows));

        panel.Destroy();
        harness.PumpUntil(() => harness.Windows.LayerWindows.Count == 0, "the host window outlived its layer surface");
    }

    [AvaloniaFact]
    public void A_click_in_a_backdrop_hole_reaches_the_layer_surface_under_it()
    {
        using var harness = new WayloniaHostHarness();
        var anchor = ZwlrLayerSurfaceV1.Anchor.Top | ZwlrLayerSurfaceV1.Anchor.Left;
        var panel = harness.MapLayer(width: 200, height: 100, scope: "panel", anchor: anchor);
        var backdrop = harness.MapLayer(width: 400, height: 300, scope: "backdrop", anchor: anchor);
        harness.SetInputRegion(backdrop, (0, 100, 400, 200), (200, 0, 200, 100));

        var window = harness.Windows.LayerWindows.Single(w => w.Width == 400);
        window.MouseMove(new Point(60, 200));
        harness.PumpInput();

        Assert.Equal(400, harness.Host.Seat.Pointer.Focus?.Current.Width);

        window.MouseMove(new Point(60, 40));
        harness.PumpInput();

        Assert.Equal(200, harness.Host.Seat.Pointer.Focus?.Current.Width);

        backdrop.Destroy();
        panel.Destroy();
        harness.PumpUntil(() => harness.Windows.LayerWindows.Count == 0, "a host window outlived its layer surface");
    }

    [AvaloniaFact]
    public void An_exclusive_layer_surface_keeps_the_keyboard_when_its_host_window_deactivates()
    {
        using var harness = new WayloniaHostHarness();
        var launcher = harness.MapLayer(
            width: 200,
            height: 100,
            scope: "launcher",
            keyboard: ZwlrLayerSurfaceV1.KeyboardInteractivity.Exclusive);
        harness.PumpInput();

        var focus = harness.Host.Seat.Keyboard.Focus;
        Assert.NotNull(focus);

        harness.Windows.Enqueue(new BasinInputEvent
        {
            Kind = InputKind.FocusOut,
            WindowId = harness.Windows.LayerWindows.Single().Id,
        });
        harness.PumpInput();

        Assert.Same(focus, harness.Host.Seat.Keyboard.Focus);

        harness.HideLayer(launcher);
        harness.PumpInput();

        Assert.Null(harness.Host.Seat.Keyboard.Focus);

        launcher.Destroy();
        harness.PumpUntil(() => harness.Windows.LayerWindows.Count == 0, "the host window outlived its layer surface");
    }
}
