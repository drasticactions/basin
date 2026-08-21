using Basin.Scene;
using Basin.Shell.Xdg;
using Xunit;

namespace Basin.Tests;

public class FramePerfectionTests
{
    private const int OldWidth = 40;
    private const int NewWidth = 20;

    [Theory]
    [MemberData(nameof(Renderers))]
    public void Three_windows_resized_in_one_transaction_never_show_a_mixed_frame(string renderer)
    {
        SkipWithoutGpu(renderer);
        using var host = new CompositorTestHost(160, 60, renderer);
        Transaction.ResetCounters();

        var windows = new List<ResizableWindow>();
        for (var i = 0; i < 3; i++)
        {
            windows.Add(ResizableWindow.Map(host, i * 50, Colors[i]));
        }

        host.RenderFrame();
        AssertUniform(host, windows, OldWidth);

        using var transaction = new Transaction(host.Loop, timeoutMs: 60_000);
        foreach (var window in windows)
        {
            window.BeginResize(host, transaction, NewWidth);
        }

        transaction.Seal();

        foreach (var window in windows)
        {
            host.RenderFrame();
            AssertUniform(host, windows, OldWidth);
            window.Respond(host, NewWidth);
        }

        host.PumpUntil(() => transaction.IsComplete);
        foreach (var window in windows)
        {
            window.FinishResize(host);
        }

        host.RenderFrame();
        AssertUniform(host, windows, NewWidth);

        Assert.False(transaction.TimedOut);
        Assert.Equal(0, Transaction.TimedOutCount);
        foreach (var window in windows)
        {
            Assert.Equal(ConfigureState.Committed, window.Toplevel.ServerToplevel.Xdg.ConfigureState);
            window.Dispose();
        }
    }

    [Fact]
    public void A_client_that_never_answers_releases_the_others_at_the_deadline()
    {
        using var host = new CompositorTestHost(160, 60);
        Transaction.ResetCounters();

        var responsive = ResizableWindow.Map(host, 0, Colors[0]);
        var hung = ResizableWindow.Map(host, 50, Colors[1]);

        using var transaction = new Transaction(host.Loop, timeoutMs: 5);
        responsive.BeginResize(host, transaction, NewWidth);
        hung.BeginResize(host, transaction, NewWidth);
        transaction.Seal();

        responsive.Respond(host, NewWidth);

        for (var i = 0; i < 100 && !transaction.IsComplete; i++)
        {
            host.Loop.Dispatch(20);
        }

        Assert.True(transaction.IsComplete);
        Assert.True(transaction.TimedOut);
        Assert.Equal(ConfigureState.Committed, responsive.Toplevel.ServerToplevel.Xdg.ConfigureState);
        Assert.Equal(ConfigureState.TimedOut, hung.Toplevel.ServerToplevel.Xdg.ConfigureState);

        responsive.FinishResize(host);
        hung.FinishResize(host);
        responsive.Dispose();
        hung.Dispose();
        Transaction.ResetCounters();
    }

    [Fact]
    public void A_superseding_configure_releases_the_earlier_transaction()
    {
        using var host = new CompositorTestHost(160, 60);
        var window = ResizableWindow.Map(host, 0, Colors[0]);

        using var first = new Transaction(host.Loop, timeoutMs: 60_000);
        window.BeginResize(host, first, 30);
        first.Seal();
        Assert.Equal(1, first.Outstanding);

        using var second = new Transaction(host.Loop, timeoutMs: 60_000);
        window.BeginResize(host, second, NewWidth);
        second.Seal();

        host.PumpUntil(() => first.IsComplete);
        Assert.False(first.TimedOut);
        Assert.False(second.IsComplete);

        window.Respond(host, NewWidth);
        host.PumpUntil(() => second.IsComplete);
        Assert.False(second.TimedOut);

        window.FinishResize(host);
        window.Dispose();
    }

    [Fact]
    public void A_client_that_disappears_mid_transaction_does_not_hold_the_deadline()
    {
        using var host = new CompositorTestHost(160, 60);
        Transaction.ResetCounters();
        var alive = ResizableWindow.Map(host, 0, Colors[0]);
        var doomed = ResizableWindow.Map(host, 50, Colors[1]);

        using var transaction = new Transaction(host.Loop, timeoutMs: 60_000);
        alive.BeginResize(host, transaction, NewWidth);
        doomed.BeginResize(host, transaction, NewWidth);
        transaction.Seal();

        alive.Respond(host, NewWidth);
        doomed.Toplevel.Toplevel.Destroy();
        doomed.Toplevel.XdgSurface.Destroy();
        host.PumpToServer();

        host.PumpUntil(() => transaction.IsComplete);
        Assert.False(transaction.TimedOut);
        Assert.Equal(0, Transaction.TimedOutCount);

        alive.FinishResize(host);
        doomed.FinishResize(host);
        alive.Dispose();
        doomed.Dispose();
    }

    [Fact]
    public void A_snapshot_keeps_drawing_after_its_source_surface_is_destroyed()
    {
        using var host = new CompositorTestHost(64, 64);
        var surface = host.Client.Compositor.CreateSurface();
        var buffer = host.Client.CreateBuffer(32, 32, Fill.Solid(32, 32, 0xffff0000));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, 32, 32);
        surface.Commit();
        host.PumpToServer();

        var scene = host.SurfaceScenes[0];
        var snapshot = SceneSnapshot.Capture(scene, host.Scene.Root);
        scene.Tree.Enabled = false;

        surface.Destroy();
        host.PumpToServer();

        host.RenderFrame();
        Assert.Equal(0xffff0000u, host.Pixel(10, 10));

        Assert.Null(host.Scene.SurfaceAt(10, 10));

        snapshot.Destroy();
        host.RenderFrame();
        Assert.NotEqual(0xffff0000u, host.Pixel(10, 10));
    }

    [Fact]
    public void A_timed_out_configure_does_not_leave_a_later_one_matching_the_stale_size()
    {
        using var host = new CompositorTestHost(160, 60);
        Transaction.ResetCounters();
        var window = ResizableWindow.Map(host, 0, Colors[0]);
        var xdg = window.Toplevel.ServerToplevel.Xdg;

        using var first = new Transaction(host.Loop, timeoutMs: 5);
        window.BeginResize(host, first, 30);
        first.Seal();
        for (var i = 0; i < 100 && !first.IsComplete; i++)
        {
            host.Loop.Dispatch(20);
        }

        Assert.True(first.TimedOut);
        Assert.Equal(ConfigureState.TimedOut, xdg.ConfigureState);

        using var second = new Transaction(host.Loop, timeoutMs: 5);
        window.BeginResize(host, second, NewWidth);
        second.Seal();

        window.Respond(host, 30);
        for (var i = 0; i < 100 && !second.IsComplete; i++)
        {
            host.Loop.Dispatch(20);
        }

        Assert.True(second.IsComplete);
        window.FinishResize(host);

        Assert.True(xdg.ConfigureState is ConfigureState.Committed or ConfigureState.TimedOut);
        window.Dispose();
        Transaction.ResetCounters();
    }

    public static TheoryData<string> Renderers =>
        new() { "pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite", "impeller" };

    private static readonly uint[] Colors = [0xffff0000, 0xff00ff00, 0xff0000ff];

    private static void SkipWithoutGpu(string renderer) => CompositorTestHost.SkipUnlessRunnable(renderer);

    private static void AssertUniform(CompositorTestHost host, List<ResizableWindow> windows, int width)
    {
        for (var i = 0; i < windows.Count; i++)
        {
            var left = windows[i].X;
            var color = Colors[i];
            Assert.Equal(color, host.Pixel(left + 1, 10));
            Assert.Equal(color, host.Pixel(left + width - 1, 10));
            Assert.NotEqual(color, host.Pixel(left + width + 1, 10));
        }
    }
}

internal sealed class ResizableWindow : IDisposable
{
    private SceneSnapshot? _snapshot;
    private SceneSurface? _scene;

    public required MappedToplevel Toplevel { get; init; }

    public required int X { get; init; }

    public required uint Color { get; init; }

    public static ResizableWindow Map(CompositorTestHost host, int x, uint color)
    {
        var toplevel = MappedToplevel.Map(host, host.Client, 40, 40, color);
        var window = new ResizableWindow { Toplevel = toplevel, X = x, Color = color };
        window._scene = host.SurfaceScenes.Find(s => s.Surface == toplevel.ServerSurface);
        window._scene!.Tree.SetPosition(x, 0);
        return window;
    }

    public void BeginResize(CompositorTestHost host, Transaction transaction, int width)
    {
        _snapshot?.Destroy();
        _snapshot = SceneSnapshot.Capture(_scene!, host.Scene.Root);
        _snapshot.Tree.SetPosition(X, 0);
        _scene!.Tree.Enabled = false;

        Toplevel.ServerToplevel.SetSize(width, 40);
        Toplevel.ServerToplevel.Xdg.SendConfigure(transaction);

        _scene.SendFrameDone(0);
        host.PumpToClient();
    }

    public void Respond(CompositorTestHost host, int width)
    {
        var buffer = host.Client.CreateBuffer(width, 40, Fill.Solid(width, 40, Color));
        Toplevel.Surface.Attach(buffer.Proxy, 0, 0);
        Toplevel.Surface.Damage(0, 0, width, 40);
        Toplevel.Surface.Commit();
        host.PumpToServer();
    }

    public void FinishResize(CompositorTestHost host)
    {
        _scene!.Tree.Enabled = true;
        _snapshot?.Destroy();
        _snapshot = null;
    }

    public void Dispose()
    {
        _snapshot?.Destroy();
        _snapshot = null;
    }
}
