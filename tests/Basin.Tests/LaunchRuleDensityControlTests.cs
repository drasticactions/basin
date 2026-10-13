using Basin.Capabilities;
using Basin.Config;
using Basin.Diagnostics;
using Xunit;

namespace Basin.Tests;

public sealed class LaunchRuleDensityControlTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("basin-density-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Config => Path.Combine(_directory, "compositor.toml");

    private const string Commented = """
        # the compositor's own comment
        [frame]
        style    = "flat"   # aligned, and kept

        """;

    [Fact]
    public void The_writer_adds_a_rule_changes_it_in_place_and_keeps_every_other_byte()
    {
        File.WriteAllText(Config, Commented);
        Assert.Equal("exec=xfig x11=low-density", LaunchRuleDensityControl.Write(Config, null, "xfig", X11Density.Low));
        var first = File.ReadAllText(Config);
        Assert.StartsWith(Commented, first, StringComparison.Ordinal);

        Assert.Equal("exec=xfig x11=native", LaunchRuleDensityControl.Write(Config, null, "xfig", X11Density.Native));
        Assert.Equal("desktop_id=leafpad.desktop x11=low-density", LaunchRuleDensityControl.Write(Config, "leafpad", null, X11Density.Low));
        var text = File.ReadAllText(Config);
        Assert.StartsWith(Commented, text, StringComparison.Ordinal);
        var document = TomlDocument.Parse(text);
        Assert.Equal(2, document.TableCount(LaunchRules.Table));

        var rules = LaunchRules.Load(Config, BasinLog.For("test"));
        Assert.Equal(X11Density.Native, rules.For(null, "xfig"));
        Assert.Equal(X11Density.Low, rules.For("leafpad.desktop", "leafpad"));
    }

    [Fact]
    public void A_rule_that_names_both_keys_is_not_taken_for_a_one_key_rule()
    {
        File.WriteAllText(Config, "[[launch]]\ndesktop_id = \"xfig.desktop\"\nexec = \"xfig\"\nx11 = \"native\"\n");
        _ = LaunchRuleDensityControl.Write(Config, null, "xfig", X11Density.Low);
        var document = TomlDocument.Parse(File.ReadAllText(Config));
        Assert.Equal(2, document.TableCount(LaunchRules.Table));
        Assert.Equal("\"native\"", document.RawValueInTable(LaunchRules.Table, 0, "x11"));
    }

    private sealed class Rig : IDisposable
    {
        public Rig(LaunchRuleDensityControlTests owner)
        {
            Host = XWaylandDisplayTests.ScaledHost(2);
            Session = new XWaylandDisplayTests.Session(Host);
            Tracker = XWaylandDisplayTests.Tracker(Host, Session.Module);
            Control = new LaunchRuleDensityControl(() => Session.Model, Tracker, Host.Loop, () => owner.Config, BasinLog.For("test"))
            {
                Grace = TimeSpan.FromSeconds(2),
            };
        }

        public CompositorTestHost Host { get; }

        public XWaylandDisplayTests.Session Session { get; }

        public ProcessTracker Tracker { get; }

        public LaunchRuleDensityControl Control { get; }

        public X11DensityResult Set(XWaylandWindowInfo window, X11Density density)
        {
            X11DensityResult? result = null;
            Assert.True(Control.TrySet(window.Id, density, r => result = r, out var error), error);
            XWaylandTestSupport.Pump(Host, () => result is not null);
            return result!.Value;
        }

        public XWaylandWindowInfo[] Windows(X11Density density)
        {
            var buffer = new ToplevelInfo[16];
            var count = Session.Model.Enumerate(buffer);
            return [.. buffer[..count].Where(w => w.X11Density == density).Select(w => new XWaylandWindowInfo(w.Id, w.Pid))];
        }

        public void Dispose()
        {
            Control.Dispose();
            Tracker.TerminateAll(TimeSpan.FromSeconds(2));
            Tracker.Dispose();
            Session.Dispose();
            Host.Dispose();
        }
    }

    public readonly record struct XWaylandWindowInfo(ulong Id, uint Pid);

    [Fact]
    public void A_tracked_application_is_closed_and_started_again_on_the_other_server()
    {
        XWaylandDisplayTests.SkipUnlessTools();
        using var rig = new Rig(this);
        var first = rig.Tracker.Spawn(new ProcessLaunch(["xeyes", "-geometry", "200x100"]), out var error);
        Assert.True(first is not null, error);
        XWaylandTestSupport.Pump(rig.Host, () => rig.Windows(X11Density.Native).Length == 1);

        var result = rig.Set(rig.Windows(X11Density.Native)[0], X11Density.Low);
        Assert.Equal(X11DensityOutcome.Relaunched, result.Outcome);
        Assert.Equal("exec=xeyes x11=low-density", result.Rule);
        Assert.NotEqual(first.LaunchId, result.LaunchId);
        XWaylandTestSupport.Pump(rig.Host, () => rig.Windows(X11Density.Low).Length == 1 && rig.Windows(X11Density.Native).Length == 0);
        Assert.False(first.IsRunning);
        Assert.Equal(X11Density.Low, LaunchRules.Load(Config, BasinLog.For("test")).For(null, "xeyes"));

        var back = rig.Set(rig.Windows(X11Density.Low)[0], X11Density.Native);
        Assert.Equal(X11DensityOutcome.Relaunched, back.Outcome);
        XWaylandTestSupport.Pump(rig.Host, () => rig.Windows(X11Density.Native).Length == 1);
        Assert.Equal(1, TomlDocument.Load(Config).TableCount(LaunchRules.Table));
    }

    [Fact]
    public void An_untracked_application_gets_the_rule_and_keeps_running()
    {
        XWaylandDisplayTests.SkipUnlessTools();
        using var rig = new Rig(this);
        using var eyes = XWaylandTestSupport.Start("xeyes", rig.Session.Native.DisplayName, "-geometry", "200x100");
        try
        {
            XWaylandTestSupport.Pump(rig.Host, () => rig.Windows(X11Density.Native).Length == 1);
            var result = rig.Set(rig.Windows(X11Density.Native)[0], X11Density.Low);
            Assert.Equal(X11DensityOutcome.AppliesNextStart, result.Outcome);
            rig.Host.Loop.Dispatch(200);
            Assert.False(eyes.HasExited);
            Assert.Single(rig.Windows(X11Density.Native));
        }
        finally
        {
            eyes.Kill();
            eyes.WaitForExit(2000);
        }
    }

    [Fact]
    public void A_process_that_does_not_exit_is_left_alone_and_reported_as_still_running()
    {
        XWaylandDisplayTests.SkipUnlessTools();
        using var rig = new Rig(this);
        var process = rig.Tracker.Spawn(new ProcessLaunch(["/bin/sh", "-c", "trap '' TERM; xeyes -geometry 200x100; sleep 30"]), out var error);
        Assert.True(process is not null, error);
        XWaylandTestSupport.Pump(rig.Host, () => rig.Windows(X11Density.Native).Length == 1);

        var result = rig.Set(rig.Windows(X11Density.Native)[0], X11Density.Low);
        Assert.Equal(X11DensityOutcome.StillRunning, result.Outcome);
        Assert.True(process.IsRunning);
        Assert.Empty(rig.Windows(X11Density.Low));
        Assert.Equal("exec=trap x11=low-density", result.Rule);
        Assert.Equal(X11Density.Low, LaunchRules.Load(Config, BasinLog.For("test")).ForArgv(null, process.Argv));
    }

    [Fact]
    public void A_process_with_two_windows_is_closed_once_and_started_once()
    {
        XWaylandDisplayTests.SkipUnlessTools();
        using var rig = new Rig(this);
        var process = rig.Tracker.Spawn(new ProcessLaunch(["/bin/sh", "-c", "xeyes -geometry 200x100 & xeyes -geometry 120x80; wait"]), out var error);
        Assert.True(process is not null, error);
        XWaylandTestSupport.Pump(rig.Host, () => rig.Windows(X11Density.Native).Length == 2);

        var result = rig.Set(rig.Windows(X11Density.Native)[0], X11Density.Low);
        Assert.Equal(X11DensityOutcome.Relaunched, result.Outcome);
        XWaylandTestSupport.Pump(rig.Host, () => rig.Windows(X11Density.Low).Length == 2);
        Assert.Empty(rig.Windows(X11Density.Native));
        Assert.Equal(2, rig.Tracker.Processes.Count);
        Assert.Single(rig.Tracker.Processes, p => p.IsRunning);
    }

    [Fact]
    public void A_window_already_on_the_target_server_is_unchanged()
    {
        XWaylandDisplayTests.SkipUnlessTools();
        using var rig = new Rig(this);
        var process = rig.Tracker.Spawn(new ProcessLaunch(["xeyes", "-geometry", "200x100"]) { X11 = X11Density.Low }, out var error);
        Assert.True(process is not null, error);
        XWaylandTestSupport.Pump(rig.Host, () => rig.Windows(X11Density.Low).Length == 1);
        var result = rig.Set(rig.Windows(X11Density.Low)[0], X11Density.Low);
        Assert.Equal(X11DensityOutcome.Unchanged, result.Outcome);
        Assert.True(process.IsRunning);
    }
}
