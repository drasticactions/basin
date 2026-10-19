using System.Text.Json;
using Basin.Capabilities;
using Basin.Ipc;
using Basin.WindowManager;
using Basin.WindowManager.Ipc;
using Xunit;

namespace Basin.Tests;

public sealed class WmPartsTests
{
    [Fact]
    public void A_hit_test_scan_gives_each_part_its_box_in_layout_coordinates()
    {
        var parts = WmPartScanner.Scan(new Rect(100, 50, 40, 30), (x, y) =>
            y < 10 ? (x < 10 ? FramePart.Close : FramePart.Title)
            : x < 2 ? WmPartScanner.EdgePart(Edges.Left)
            : WmPartScanner.EdgePart(Edges.None));
        Assert.Equal(
            [(FramePart.Title, new Box(110, 50, 30, 10)), (FramePart.Close, new Box(100, 50, 10, 10)), (FramePart.Left, new Box(100, 60, 2, 20))],
            parts);
        Assert.Equal(FramePart.BottomRight, WmPartScanner.EdgePart(Edges.Bottom | Edges.Right));
    }

    [Fact]
    public void A_parts_source_alone_answers_diag_parts_and_targets()
    {
        var source = new Source();
        using var rig = new IpcTestRig(services => services.Use<IWindowPartsSource>(source));
        rig.Server.Start();
        Assert.Contains(IpcMethodNames.DiagParts, rig.Server.Methods.Names);
        Assert.DoesNotContain(IpcMethodNames.DiagRecordWindow, rig.Server.Methods.Names);
        var peer = rig.Connect();

        using var parts = JsonDocument.Parse(peer.Call("""{"method":"diag/parts","params":{"window":"foot"}}"""));
        var close = parts.RootElement.GetProperty("result").GetProperty("parts")[0];
        Assert.Equal("close", close.GetProperty("part").GetString());
        Assert.Equal(14, close.GetProperty("box").GetProperty("x").GetInt32());
        using var byTitle = JsonDocument.Parse(peer.Call("""{"method":"diag/parts","params":{"window":"other"}}"""));
        Assert.Equal("xterm", byTitle.RootElement.GetProperty("result").GetProperty("app_id").GetString());
        using var targets = JsonDocument.Parse(peer.Call("""{"method":"diag/targets","params":{}}"""));
        Assert.Equal(2, targets.RootElement.GetProperty("result").GetProperty("targets").GetArrayLength());
    }

    [Fact]
    public void The_window_manager_loop_carries_the_control_socket_timers_and_idles()
    {
        var loop = new FakeLoop();
        var adapter = new WmIpcLoop(loop);
        var fired = 0;
        var timer = adapter.AddTimer(() => fired++);
        timer.UpdateTimer(5);
        adapter.AddIdle(() => fired++);
        adapter.DeferDestroy(new Disposable(() => fired++));
        loop.RunIdles();
        loop.Fire();
        Assert.Equal(3, fired);
        Assert.Throws<NotSupportedException>(() => adapter.Fd);
    }

    private sealed class Source : IWindowPartsSource
    {
        public void CollectWindows(List<WindowParts> into)
        {
            into.Add(new WindowParts("foot", "a terminal", new Box(10, 10, 708, 514), [(FramePart.Close, new Box(14, 14, 19, 19))]));
            into.Add(new WindowParts("xterm", "other", new Box(0, 0, 100, 80), []));
        }
    }

    private sealed class Disposable(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    private sealed class FakeLoop : IWmEventLoop
    {
        private readonly List<Action> _idles = [];
        private readonly List<Action> _timers = [];

        public IWmEventSource AddFd(int fd, WmFdReadiness events, Action<int, WmFdReadiness> handler) => new Source2();

        public IWmEventSource AddTimer(Action handler)
        {
            _timers.Add(handler);
            return new Source2();
        }

        public void AddIdle(Action handler) => _idles.Add(handler);

        public void RunIdles()
        {
            foreach (var idle in _idles.ToArray())
            {
                idle();
            }
        }

        public void Fire()
        {
            foreach (var timer in _timers.ToArray())
            {
                timer();
            }
        }

        private sealed class Source2 : IWmEventSource
        {
            public bool IsRemoved => false;

            public void Remove()
            {
            }

            public void UpdateTimer(int delayMs)
            {
            }

            public void UpdateFd(WmFdReadiness events)
            {
            }
        }
    }
}
