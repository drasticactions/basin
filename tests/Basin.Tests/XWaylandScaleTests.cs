using System.Diagnostics;
using Basin.XWayland;
using Xunit;

namespace Basin.Tests;

public sealed class XWaylandScaleTests
{
    private static string Output(CompositorTestHost host, string display, string program, params string[] args)
    {
        var file = Path.Combine(Path.GetTempPath(), $"basin-xscale-{Guid.NewGuid():N}.txt");
        try
        {
            var command = string.Join(' ', [program, .. args.Select(static arg => $"'{arg}'")]) + $" > '{file}'";
            var info = new ProcessStartInfo("/bin/sh", ["-c", command]) { UseShellExecute = false };
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

    private sealed class Session : IDisposable
    {
        private readonly XwaylandShellGlobal _shell;

        public Session(CompositorTestHost host, XWaylandScale scale, IReadOnlyList<int>? buckets = null)
        {
            Host = host;
            _shell = new XwaylandShellGlobal(host.Display, host.Compositor);
            Server = new XWaylandServer(host.Display, host.Loop) { X11Scale = scale };
            if (buckets is not null)
            {
                Server.DpiBuckets = buckets;
            }

            Outputs = new XWaylandOutputs(Server, host.Layout, host.Compositor.ClientScales, () => null);
            Outputs.ScaleChanged += value => Wm?.SetScale(value);
            Server.Ready += fd =>
            {
                Wm = new XWaylandWm(fd, host.Loop, _shell, host.Seat, new XWaylandWmOptions
                {
                    Scale = Outputs.Scale,
                    DpiBuckets = Server.DpiBuckets,
                });
                Wm.WindowMapped += mapped => Mapped.Add(mapped);
            };
        }

        public CompositorTestHost Host { get; }

        public XWaylandServer Server { get; }

        public XWaylandOutputs Outputs { get; }

        public XWaylandWm? Wm { get; private set; }

        public List<XWaylandWindow> Mapped { get; } = [];

        public void Dispose()
        {
            Wm?.Dispose();
            Outputs.Dispose();
            Server.Dispose();
            _shell.Dispose();
        }
    }

    private static void SkipUnlessTools() =>
        Assert.SkipUnless(PrivateBus.OnPath("Xwayland") && PrivateBus.OnPath("xeyes") && PrivateBus.OnPath("xprop")
            && PrivateBus.OnPath("xdpyinfo"), "Xwayland, xeyes, xprop and xdpyinfo are needed");

    private static CompositorTestHost ScaledHost(double scale)
    {
        var host = new CompositorTestHost(640, 480);
        using var state = new OutputState();
        Assert.True(host.Output.Commit(state.SetScale(scale)));
        return host;
    }

    [Fact]
    public void An_X11_window_is_sized_in_device_pixels_and_reported_in_logical_ones()
    {
        SkipUnlessTools();
        using var host = ScaledHost(2);
        using var session = new Session(host, XWaylandScale.Auto);
        Assert.Equal(2, session.Outputs.Scale);

        using var eyes = XWaylandTestSupport.Start("xeyes", session.Server.DisplayName, "-geometry", "200x100+40+20");
        try
        {
            XWaylandTestSupport.Pump(host, () => session.Mapped.Count > 0);
            var window = session.Mapped[0];
            Assert.Equal(2, window.Surface!.ServerScale);
            Assert.Equal((200, 100), (window.X11Bounds.Width, window.X11Bounds.Height));
            Assert.Equal((100, 50), (window.Width, window.Height));
            XWaylandTestSupport.Pump(host, () => window.Surface.Current.Width == 100);

            var screen = Output(host, session.Server.DisplayName, "xdpyinfo");
            Assert.Contains("640x480 pixels", screen, StringComparison.Ordinal);

            var resources = Output(host, session.Server.DisplayName, "xprop", "-root", "RESOURCE_MANAGER");
            Assert.Contains("Xft.dpi:\\t192", resources, StringComparison.Ordinal);
            Assert.True(session.Wm!.XSettings!.IsOwner);
            Assert.Equal(192 * 1024, session.Wm.XSettings.IntegerOf("Xft/DPI"));
            Assert.Equal(2, session.Wm.XSettings.IntegerOf("Gdk/WindowScalingFactor"));

            window.Configure(30, 40, 150, 80);
            Assert.Equal(new Box(60, 80, 300, 160), window.X11Bounds);
            XWaylandTestSupport.Pump(host, () => window.Surface.Current.Width == 150);
        }
        finally
        {
            eyes.Kill();
            eyes.WaitForExit(2000);
        }
    }

    [Fact]
    public void A_configure_to_a_windows_own_geometry_keeps_its_X11_pixels_at_a_fractional_scale()
    {
        SkipUnlessTools();
        using var host = ScaledHost(2.1);
        using var session = new Session(host, XWaylandScale.Auto);

        using var eyes = XWaylandTestSupport.Start("xeyes", session.Server.DisplayName, "-geometry", "201x101+41+21");
        try
        {
            XWaylandTestSupport.Pump(host, () => session.Mapped.Count > 0);
            var window = session.Mapped[0];
            Assert.Equal(new Box(41, 21, 201, 101), window.X11Bounds);
            Assert.Equal((96, 48), (window.Width, window.Height));

            window.Configure(window.X, window.Y, window.Width, window.Height);
            Assert.Equal(new Box(41, 21, 201, 101), window.X11Bounds);

            window.Configure(window.X + 10, window.Y, window.Width + 10, window.Height);
            Assert.Equal(new Box(63, 21, 223, 101), window.X11Bounds);
        }
        finally
        {
            eyes.Kill();
            eyes.WaitForExit(2000);
        }
    }

    [Fact]
    public void A_fixed_scale_and_buckets_set_the_published_dpi()
    {
        SkipUnlessTools();
        using var host = ScaledHost(1);
        using var session = new Session(host, XWaylandScale.Fixed(1.5), [72, 96, 160, 240]);
        Assert.Equal(1.5, session.Outputs.Scale);

        using var eyes = XWaylandTestSupport.Start("xeyes", session.Server.DisplayName, "-geometry", "150x150");
        try
        {
            XWaylandTestSupport.Pump(host, () => session.Mapped.Count > 0);
            var resources = Output(host, session.Server.DisplayName, "xprop", "-root", "RESOURCE_MANAGER");
            Assert.Contains("Xft.dpi:\\t160", resources, StringComparison.Ordinal);
            Assert.Equal(1, session.Wm!.XSettings!.IntegerOf("Gdk/WindowScalingFactor"));
            Assert.Equal((100, 100), (session.Mapped[0].Width, session.Mapped[0].Height));

            session.Server.X11Scale = XWaylandScale.Fixed(2);
            Assert.Equal(2, session.Wm.Scale);
            Assert.Equal(2, session.Mapped[0].Surface!.ServerScale);
            Assert.Equal(new Box(0, 0, 200, 200), session.Mapped[0].X11Bounds with { X = 0, Y = 0 });
            Assert.Equal(2, session.Wm.XSettings.IntegerOf("Gdk/WindowScalingFactor"));
            Assert.Equal(160 * 1024, session.Wm.XSettings.IntegerOf("Xft/DPI"));
        }
        finally
        {
            eyes.Kill();
            eyes.WaitForExit(2000);
        }
    }

    [Fact]
    public void Dpi_buckets_snap_to_the_nearest()
    {
        int[] buckets = [72, 96, 160, 240];
        Assert.Equal(96, XWaylandDpi.For(112 / 96.0, buckets));
        Assert.Equal(160, XWaylandDpi.For(188 / 96.0, buckets));
        Assert.Equal(144, XWaylandDpi.For(1.5, null));
        Assert.Equal(2, XWaylandDpi.WindowScalingFactor(2));
        Assert.Equal(1, XWaylandDpi.WindowScalingFactor(1.5));
    }

    [Fact]
    public void The_resource_database_keeps_other_keys_and_replaces_ours()
    {
        var merged = XResourceDatabase.Merge(
            "Xft.dpi:\t96\n*background:\tblack\nXcursor.size:\t24\n",
            [("Xft.dpi", "192"), ("Xcursor.size", "48")]);
        Assert.Equal("*background:\tblack\nXft.dpi:\t192\nXcursor.size:\t48\n", merged);
    }

    [Fact]
    public void The_settings_encoding_follows_the_xsettings_layout()
    {
        var settings = new SortedDictionary<string, XSettingsManager.Setting>(StringComparer.Ordinal)
        {
            ["Xft/DPI"] = new XSettingsManager.Setting(196608, null) { LastChange = 3 },
            ["Net/ThemeName"] = new XSettingsManager.Setting(0, "Adwaita") { LastChange = 4 },
        };
        var bytes = XSettingsManager.Encode(settings, 7);

        Assert.Equal(0, bytes[0]);
        Assert.Equal(7u, BitConverter.ToUInt32(bytes, 4));
        Assert.Equal(2u, BitConverter.ToUInt32(bytes, 8));
        Assert.Equal(1, bytes[12]);
        Assert.Equal(13, BitConverter.ToUInt16(bytes, 14));
        Assert.Equal("Net/ThemeName", System.Text.Encoding.ASCII.GetString(bytes, 16, 13));
        Assert.Equal(4u, BitConverter.ToUInt32(bytes, 32));
        Assert.Equal(7u, BitConverter.ToUInt32(bytes, 36));
        Assert.Equal("Adwaita", System.Text.Encoding.ASCII.GetString(bytes, 40, 7));
        Assert.Equal(0, bytes[48]);
        Assert.Equal(0, bytes.Length % 4);
    }
}
