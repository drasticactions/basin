using Basin.Capabilities;
using Xunit;

namespace Basin.Tests;

public sealed class SurfaceDiagnosticsTests
{
    [Fact]
    public void A_commit_traces_its_buffer_damage_and_callbacks_and_the_callbacks_firing()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client, 60, 50, 0xFF0000FF);
        var other = MappedToplevel.Map(host, host.Client, 30, 20, 0xFF00FF00);
        host.PumpToServer();
        var diagnostics = new SurfaceDiagnostics(host.Compositor);
        var traces = new List<CommitTrace>();
        using (diagnostics.WatchCommits(surface => surface == window.ServerSurface, traces.Add))
        {
            using var callback = window.Surface.Frame();
            window.Surface.Attach(window.Buffer.Proxy, 0, 0);
            window.Surface.DamageBuffer(2, 3, 10, 11);
            window.Surface.Commit();
            other.Surface.Attach(other.Buffer.Proxy, 0, 0);
            other.Surface.Commit();
            host.PumpToServer();
            window.ServerSurface.SendFrameDone(77);
        }

        var commit = Assert.Single(traces, trace => trace.Kind == "commit");
        Assert.Equal((60, 50, 1), (commit.Width, commit.Height, commit.FrameCallbacks));
        Assert.Contains(new Box(2, 3, 10, 11), commit.Damage);
        var done = Assert.Single(traces, trace => trace.Kind == "frame-done");
        Assert.Equal((77u, 1), (done.TimestampMs, done.FrameCallbacks));

        window.Surface.Commit();
        host.PumpToServer();
        Assert.Equal(2, traces.Count);
    }
}
