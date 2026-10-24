using Basin.Host;
using Basin.UI.Avalonia;
using Xunit;

namespace Basin.Tests;

public sealed class ScreenReaderTests
{
    private static void WithBus(Action body)
    {
        var saved = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", "unix:path=/nonexistent/bus");
        try
        {
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", saved);
        }
    }

    [Fact]
    public void The_availability_ladder_names_the_first_missing_piece()
    {
        WithBus(() =>
        {
            using var host = new CompositorTestHost();
            using var processes = new ProcessTracker(host.Loop);

            using (var off = new ScreenReader(processes, ["sleep"], "off"))
            {
                off.AccessibilityStarted(true);
                Assert.Equal("the screen reader is off", off.Unavailable);
            }

            using var reader = new ScreenReader(processes, ["sleep"], "toggle");
            Assert.Equal("the accessibility tree is not on the bus", reader.Unavailable);
            reader.AccessibilityStarted(true);
            Assert.Null(reader.Unavailable);
            Assert.False(reader.IsRunning);

            reader.Argv = ["basin-no-such-reader"];
            Assert.Equal("basin-no-such-reader is not installed", reader.Unavailable);
            reader.Argv = [];
            Assert.Equal("no screen reader command", reader.Unavailable);
            reader.Start();
            Assert.False(reader.IsRunning);
        });

        var saved = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", null);
        try
        {
            using var host = new CompositorTestHost();
            using var processes = new ProcessTracker(host.Loop);
            using var reader = new ScreenReader(processes, ["sleep"], "toggle");
            reader.AccessibilityStarted(true);
            Assert.Equal("there is no session bus", reader.Unavailable);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", saved);
        }
    }

    [Fact]
    public void Mode_on_starts_the_reader_once_the_tree_is_on_the_bus_and_stop_ends_it()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the tracker reaps through a pidfd");
        WithBus(() =>
        {
            using var host = new CompositorTestHost();
            using var processes = new ProcessTracker(host.Loop);
            using var reader = new ScreenReader(processes, ["sleep", "30"], "on");
            var started = 0;
            var stopped = 0;
            reader.Started += _ => started++;
            reader.Stopped += () => stopped++;

            reader.AccessibilityStarted(true);
            Assert.True(reader.IsRunning);
            Assert.Equal(1, started);
            var pid = reader.Process!.Pid;

            reader.Toggle();

            Assert.False(reader.IsRunning);
            Assert.Equal(1, stopped);
            Assert.False(processes.Owns(pid));
        });
    }
}
