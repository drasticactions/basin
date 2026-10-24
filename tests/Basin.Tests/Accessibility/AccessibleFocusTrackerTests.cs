using AccessKit;
using AccessKit.Unix;
using Basin.Accessibility;
using Basin.Capabilities;
using Xunit;

namespace Basin.Tests;

public sealed class AccessibleFocusTrackerTests
{
    private sealed class NoToplevels : IToplevelModel
    {
        public int Enumerate(Span<ToplevelInfo> toplevels) => 0;

        public bool TryGet(ulong toplevelId, out ToplevelInfo info)
        {
            info = default;
            return false;
        }

        public void AddObserver(IToplevelObserver observer)
        {
        }

        public void RemoveObserver(IToplevelObserver observer)
        {
        }

        public bool Request(ulong toplevelId, in ToplevelRequest request) => false;
    }

    private static T Run<T>(EpollEventLoop loop, Task<T> task)
    {
        while (!task.IsCompleted)
        {
            loop.Dispatch(10);
        }

        return task.GetAwaiter().GetResult();
    }

    private static void Run(EpollEventLoop loop, Task task)
    {
        while (!task.IsCompleted)
        {
            loop.Dispatch(10);
        }

        task.GetAwaiter().GetResult();
    }

    private static TreeUpdate Tree(ulong focus)
    {
        var update = new TreeUpdate(1) { Tree = new TreeInfo(1) { ToolkitName = "focus-test" }, Focus = focus };
        var root = new Node(Role.Window) { Label = "Focus test" };
        root.SetChildren([2, 3]);
        update.Push(1, root);
        update.Push(2, new Node(Role.Button) { Label = "First", Bounds = new Rect(10, 20, 90, 50) });
        update.Push(3, new Node(Role.Button) { Label = "Second", Bounds = new Rect(200, 120, 260, 150) });
        return update;
    }

    [Fact]
    public void A_focus_move_is_reported_with_the_control_box()
    {
        using var loop = new EpollEventLoop();
        var bus = Run(loop, AccessibilityBus.ForProcessAsync());
        using var tracker = AccessibleFocusTracker.TryStart(loop, new NoToplevels(), bus.SessionAddress, out var reason);
        Assert.True(tracker is not null, reason);
        Run(loop, tracker.Started);
        var moves = new List<(Box Box, AccessibleFocusKind Kind)>();
        tracker.Moved += (box, kind) => moves.Add((box, kind));

        using var adapter = new UnixAdapter(() => Tree(2), static _ => { }, static () => { }, new UnixAdapterOptions { ValidateUpdates = true });
        adapter.SetRootWindowBounds(new Rect(0, 0, 640, 480), new Rect(0, 0, 640, 480));
        adapter.UpdateWindowFocusState(true);
        using var reader = Run(loop, bus.ConnectAsync());
        Run(loop, reader.FindAsync("First", "push button"));

        adapter.UpdateIfActive(() => Tree(3));
        for (var i = 0; i < 300 && !moves.Exists(m => m.Box == new Box(200, 120, 60, 30)); i++)
        {
            loop.Dispatch(10);
        }

        Assert.Contains((new Box(200, 120, 60, 30), AccessibleFocusKind.Focus), moves);
        adapter.UpdateWindowFocusState(false);
    }
}
