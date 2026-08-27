using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basin.Avalonia;
using Basin.Shell.Xdg;
using Xunit;

namespace Waylonia.Tests;

public sealed class ScreenWindowTests
{
    private sealed class GuestPolicy : IAvaloniaShellPolicy
    {
        public void PlaceWindow(Window window, ToplevelInfo info)
        {
        }

        public string? ChooseScreen(IReadOnlyCollection<HostScreenInfo> screens) => null;

        public void CloseRequested(XdgToplevelWindow toplevel, int requests) => toplevel.Close();

        public ScreenSurfaceKind Classify(ToplevelInfo info) =>
            info.AppId == "guest" ? ScreenSurfaceKind.Screen : ScreenSurfaceKind.Application;
    }

    [AvaloniaFact]
    public void A_guest_toplevel_becomes_the_screen_window()
    {
        using var harness = new WayloniaHostHarness();
        harness.Windows.Policy = new GuestPolicy();
        ToplevelWindow? announced = null;
        var announcements = 0;
        harness.Windows.ScreenWindowChanged += window =>
        {
            announced = window;
            announcements++;
        };

        var toplevel = harness.MapToplevel(width: 640, height: 480, appId: "guest");

        Assert.Equal(1, announcements);
        Assert.NotNull(announced);
        Assert.Same(Assert.Single(harness.Windows.Windows), announced);
        Assert.Same(announced, harness.Windows.ScreenWindow);

        toplevel.Destroy();
        harness.PumpUntil(() => harness.Windows.Windows.Count == 0, "the screen window outlived its toplevel");
        Assert.Null(announced);
        Assert.Null(harness.Windows.ScreenWindow);
    }

    [AvaloniaFact]
    public void An_application_toplevel_is_not_a_screen_window()
    {
        using var harness = new WayloniaHostHarness();
        harness.Windows.Policy = new GuestPolicy();
        var announcements = 0;
        harness.Windows.ScreenWindowChanged += _ => announcements++;

        var toplevel = harness.MapToplevel(appId: "notes");

        Assert.Equal(0, announcements);
        Assert.Null(harness.Windows.ScreenWindow);

        toplevel.Destroy();
        harness.PumpUntil(() => harness.Windows.Windows.Count == 0, "the host window outlived its toplevel");
    }

    [AvaloniaFact]
    public void The_screen_window_keeps_the_frame_the_guest_does_not_draw()
    {
        using var harness = new WayloniaHostHarness();
        harness.Windows.Policy = new GuestPolicy();

        var toplevel = harness.MapToplevel(appId: "guest");
        var window = Assert.Single(harness.Windows.Windows);

        Assert.Equal(WindowDecorations.Full, window.WindowDecorations);

        toplevel.Destroy();
        harness.PumpUntil(() => harness.Windows.Windows.Count == 0, "the screen window outlived its toplevel");
    }

    [AvaloniaFact]
    public void An_overridden_title_survives_the_guest_setting_its_own()
    {
        using var harness = new WayloniaHostHarness();
        harness.Windows.Policy = new GuestPolicy();

        var toplevel = harness.MapToplevel(title: "sway", appId: "guest");
        var window = Assert.Single(harness.Windows.Windows);
        window.OverrideTitle("plasma @ lab");
        Assert.Equal("plasma @ lab", window.Title);

        toplevel.Toplevel.SetTitle("wlroots - WL-1");
        toplevel.Surface.Commit();
        harness.PumpUntil(() => harness.Windows.Windows.Count == 1, "the screen window went away");
        Assert.Equal("plasma @ lab", window.Title);

        window.OverrideTitle(null);
        harness.PumpUntil(() => window.Title == "wlroots - WL-1", "the client's own title never came back");

        toplevel.Destroy();
        harness.PumpUntil(() => harness.Windows.Windows.Count == 0, "the screen window outlived its toplevel");
    }

    [AvaloniaFact]
    public void The_guest_sees_the_screen_window_s_size_as_its_own_output_mode()
    {
        using var harness = new WayloniaHostHarness();
        harness.Windows.Policy = new GuestPolicy();
        var modes = new List<(int Width, int Height)>();
        harness.Client.Outputs[0].ModeEvent += (_, e) => modes.Add((e.Width, e.Height));

        var toplevel = harness.MapToplevel(width: 640, height: 480, appId: "guest");
        harness.PumpUntil(
            () => modes.Contains((640, 480)), "the guest never saw the screen window's size as a mode");

        toplevel.Destroy();
        harness.PumpUntil(() => harness.Windows.Windows.Count == 0, "the screen window outlived its toplevel");
    }

    [AvaloniaFact]
    public void An_application_never_moves_the_output_it_is_on()
    {
        using var harness = new WayloniaHostHarness();
        harness.Windows.Policy = new GuestPolicy();
        var modes = new List<(int Width, int Height)>();
        harness.Client.Outputs[0].ModeEvent += (_, e) => modes.Add((e.Width, e.Height));

        var toplevel = harness.MapToplevel(width: 200, height: 150, appId: "notes");
        for (var i = 0; i < 20; i++)
        {
            harness.Pump();
        }

        Assert.DoesNotContain((200, 150), modes);

        toplevel.Destroy();
        harness.PumpUntil(() => harness.Windows.Windows.Count == 0, "the host window outlived its toplevel");
    }
}
