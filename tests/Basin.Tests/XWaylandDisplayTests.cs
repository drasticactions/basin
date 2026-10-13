using System.Diagnostics;
using Basin.Capabilities;
using Basin.XWayland;
using Xunit;

namespace Basin.Tests;

public sealed class XWaylandDisplayTests
{
    internal sealed class Session : IDisposable
    {
        public Session(CompositorTestHost host, bool lowDensity = true)
        {
            Host = host;
            Module = new XWaylandModule { IncludeLowDensity = lowDensity, IncludeKeyboardGrab = false };
            Services = new BasinServices(host.Display, host.Loop)
                .Use(host.Layout)
                .Use(host.Compositor)
                .Use(host.Seat)
                .Use<IToplevelModel>(Model)
                .Install(Module)
                .Freeze();
            foreach (var display in Module.Displays)
            {
                Mapped[display] = [];
                display.WindowManagerReady += wm => wm.WindowMapped += window => Mapped[display].Add(window);
            }
        }

        public CompositorTestHost Host { get; }

        public XWaylandModule Module { get; }

        public BasinServices Services { get; }

        public AggregateToplevelModel Model { get; } = new();

        public Dictionary<XWaylandDisplay, List<XWaylandWindow>> Mapped { get; } = [];

        public XWaylandDisplay Native => Module.Native!;

        public XWaylandDisplay Low => Module.LowDensity!;

        public void Dispose() => Services.Dispose();
    }

    internal static void SkipUnlessTools() =>
        Assert.SkipUnless(PrivateBus.OnPath("Xwayland") && PrivateBus.OnPath("xeyes") && PrivateBus.OnPath("xprop"),
            "Xwayland, xeyes and xprop are needed");

    internal static CompositorTestHost ScaledHost(double scale)
    {
        var host = new CompositorTestHost(640, 480);
        using var state = new OutputState();
        Assert.True(host.Output.Commit(state.SetScale(scale)));
        return host;
    }

    private static string Resources(CompositorTestHost host, string display)
    {
        var file = Path.Combine(Path.GetTempPath(), $"basin-xdisplay-{Guid.NewGuid():N}.txt");
        try
        {
            var info = new ProcessStartInfo("/bin/sh", ["-c", $"xprop -root RESOURCE_MANAGER > '{file}'"]) { UseShellExecute = false };
            info.Environment["DISPLAY"] = display;
            info.Environment.Remove("WAYLAND_DISPLAY");
            using var process = Process.Start(info)!;
            XWaylandTestSupport.Pump(host, () => process.HasExited);
            return File.ReadAllText(file);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static void Stop(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill();
        }

        process.WaitForExit(2000);
    }

    [Fact]
    public void The_module_holds_a_native_and_a_low_density_display()
    {
        using var host = ScaledHost(2);
        using var session = new Session(host);
        Assert.Equal(2, session.Module.Displays.Count);
        Assert.Equal(X11Density.Native, session.Native.Density);
        Assert.Equal(X11Density.Low, session.Low.Density);
        Assert.NotEqual(session.Native.DisplayName, session.Low.DisplayName);
        Assert.Equal(2, session.Native.Scale);
        Assert.Equal(1, session.Low.Scale);
        Assert.Same(session.Low, session.Module.DisplayFor(X11Density.Low));
        Assert.Same(session.Native, session.Module.DisplayFor(X11Density.Native));
        Assert.Equal("48", session.Native.ChildEnvironment()["XCURSOR_SIZE"]);
        Assert.Equal("24", session.Low.ChildEnvironment()["XCURSOR_SIZE"]);
    }

    [Fact]
    public void Without_low_density_the_module_holds_one_display()
    {
        using var host = ScaledHost(2);
        using var session = new Session(host, lowDensity: false);
        Assert.Single(session.Module.Displays);
        Assert.Null(session.Module.LowDensity);
        Assert.Same(session.Native, session.Module.DisplayFor(X11Density.Low));
    }

    [Fact]
    public void At_scale_one_a_low_density_launch_goes_to_the_native_display()
    {
        using var host = ScaledHost(1);
        using var session = new Session(host);
        Assert.Same(session.Native, session.Module.DisplayFor(X11Density.Low));
        Assert.False(session.Low.Server.IsRunning);
    }

    [Fact]
    public void A_client_on_the_low_density_display_does_not_start_the_native_one()
    {
        SkipUnlessTools();
        using var host = ScaledHost(2);
        using var session = new Session(host);
        using var eyes = XWaylandTestSupport.Start("xeyes", session.Low.DisplayName, "-geometry", "200x100");
        try
        {
            XWaylandTestSupport.Pump(host, () => session.Mapped[session.Low].Count > 0);
            Assert.True(session.Low.Server.IsRunning);
            Assert.False(session.Native.Server.IsRunning);
            Assert.Null(session.Native.WindowManager);

            var window = session.Mapped[session.Low][0];
            Assert.Equal((200, 100), (window.Width, window.Height));
            Assert.Equal((200, 100), (window.X11Bounds.Width, window.X11Bounds.Height));
            Assert.Same(session.Low, session.Module.DisplayOf(window));
            Assert.Contains("Xft.dpi:\\t96", Resources(host, session.Low.DisplayName), StringComparison.Ordinal);
            Assert.Equal(96 * 1024, session.Low.WindowManager!.XSettings!.IntegerOf("Xft/DPI"));
            Assert.Equal(1, session.Low.WindowManager.XSettings.IntegerOf("Gdk/WindowScalingFactor"));
        }
        finally
        {
            Stop(eyes);
        }
    }

    [Fact]
    public void Each_server_matches_only_its_own_serials_and_windows_get_distinct_ids()
    {
        SkipUnlessTools();
        using var host = ScaledHost(2);
        using var session = new Session(host);
        using var native = XWaylandTestSupport.Start("xeyes", session.Native.DisplayName, "-geometry", "200x100");
        using var low = XWaylandTestSupport.Start("xeyes", session.Low.DisplayName, "-geometry", "200x100");
        try
        {
            XWaylandTestSupport.Pump(host, () => session.Mapped[session.Native].Count > 0 && session.Mapped[session.Low].Count > 0);
            var nativeWindow = session.Mapped[session.Native][0];
            var lowWindow = session.Mapped[session.Low][0];
            Assert.Same(session.Native.Server.Client, nativeWindow.Surface!.Resource.Client);
            Assert.Same(session.Low.Server.Client, lowWindow.Surface!.Resource.Client);
            Assert.Equal(2, nativeWindow.Surface.ServerScale);
            Assert.Equal((100, 50), (nativeWindow.Width, nativeWindow.Height));
            Assert.Equal((200, 100), (lowWindow.Width, lowWindow.Height));

            Assert.Contains("Xft.dpi:\\t192", Resources(host, session.Native.DisplayName), StringComparison.Ordinal);
            Assert.Contains("Xft.dpi:\\t96", Resources(host, session.Low.DisplayName), StringComparison.Ordinal);
            Assert.Equal(2, session.Native.WindowManager!.XSettings!.IntegerOf("Gdk/WindowScalingFactor"));
            Assert.Equal(1, session.Low.WindowManager!.XSettings!.IntegerOf("Gdk/WindowScalingFactor"));

            var toplevels = new ToplevelInfo[8];
            var count = session.Model.Enumerate(toplevels);
            Assert.Equal(2, count);
            Assert.NotEqual(toplevels[0].Id, toplevels[1].Id);
            Assert.True(((IToplevelModel)session.Model).TryFind(lowWindow.Surface, out var lowInfo));

            Stop(native);
            XWaylandTestSupport.Pump(host, () => session.Model.Enumerate(toplevels) == 1);
            Assert.Equal(lowInfo.Id, toplevels[0].Id);
        }
        finally
        {
            Stop(native);
            Stop(low);
        }
    }

    internal static ProcessTracker Tracker(CompositorTestHost host, XWaylandModule module) =>
        new(host.Loop, () => ProcessTracker.SessionEnvironment("wayland-not-connected", module.Native!.DisplayName, null, module.ChildEnvironment()))
        {
            X11Routes = module.Route,
        };

    private static string Echo(CompositorTestHost host, ProcessTracker tracker, X11Density? density)
    {
        var file = Path.Combine(Path.GetTempPath(), $"basin-xroute-{Guid.NewGuid():N}.txt");
        try
        {
            var process = tracker.Spawn(
                new ProcessLaunch(["/bin/sh", "-c", "echo \"$DISPLAY $BASIN_X11_LOW_DENSITY_DISPLAY $XCURSOR_SIZE\""]) { LogPath = file, X11 = density },
                out var error);
            Assert.True(process is not null, error);
            XWaylandTestSupport.Pump(host, () => !process.IsRunning);
            return File.ReadAllText(file).Trim();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void A_spawned_process_sees_both_displays_and_a_low_density_launch_gets_the_second()
    {
        using var host = ScaledHost(2);
        using var session = new Session(host);
        using var tracker = Tracker(host, session.Module);
        var native = session.Native.DisplayName;
        var low = session.Low.DisplayName;
        Assert.Equal($"{native} {low} 48", Echo(host, tracker, null));
        Assert.Equal($"{native} {low} 48", Echo(host, tracker, X11Density.Native));
        Assert.Equal($"{low} {low} 24", Echo(host, tracker, X11Density.Low));
    }

    [Fact]
    public void At_scale_one_the_variable_names_the_native_display()
    {
        using var host = ScaledHost(1);
        using var session = new Session(host);
        using var tracker = Tracker(host, session.Module);
        var native = session.Native.DisplayName;
        Assert.Equal($"{native} {native} 24", Echo(host, tracker, null));
        Assert.Equal($"{native} {native} 24", Echo(host, tracker, X11Density.Low));
        Assert.False(session.Low.Server.IsRunning);
    }

    [Fact]
    public void A_launch_rule_puts_a_window_on_the_low_density_display()
    {
        SkipUnlessTools();
        using var host = ScaledHost(2);
        using var session = new Session(host);
        using var tracker = Tracker(host, session.Module);
        var rules = Basin.Config.LaunchRules.Read(new Basin.Config.TomlReader(
            (Tomlyn.Model.TomlTable)Tomlyn.Toml.ToModel("[[launch]]\nexec = \"xeyes\"\nx11 = \"low-density\"\n"),
            Basin.Diagnostics.BasinLog.For("test")));
        string[] argv = ["xeyes", "-geometry", "200x100"];
        var process = tracker.Spawn(new ProcessLaunch(argv) { X11 = rules.ForArgv(null, argv) }, out var error);
        Assert.True(process is not null, error);
        try
        {
            XWaylandTestSupport.Pump(host, () => session.Mapped[session.Low].Count > 0);
            Assert.Empty(session.Mapped[session.Native]);
            Assert.False(session.Native.Server.IsRunning);
            Assert.Contains("Xft.dpi:\\t96", Resources(host, session.Low.DisplayName), StringComparison.Ordinal);
        }
        finally
        {
            tracker.TerminateAll(TimeSpan.FromSeconds(2));
        }
    }
}
