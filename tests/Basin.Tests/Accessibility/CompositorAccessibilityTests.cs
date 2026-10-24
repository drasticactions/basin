using Basin.Accessibility;
using Basin.Capabilities;
using Xunit;

namespace Basin.Tests;

public sealed class CompositorAccessibilityTests
{
    private sealed class Panel : IAccessibleChrome
    {
        public int Presses { get; private set; }

        public string Title { get; set; } = "Panel";

        public event Action? Changed;

        public void Describe(AccessibleTreeBuilder tree)
        {
            var group = tree.Add(-1, new AccessibleNode(AccessibleRole.Group, Title, new Box(100, 50, 300, 200)));
            tree.Add(group, new AccessibleNode(AccessibleRole.Button, "Apply", new Box(120, 200, 80, 30), Press: () => Presses++));
            tree.Add(group, new AccessibleNode(AccessibleRole.CheckBox, "Wrap", new Box(120, 100, 80, 20), AccessibleStates.Checked));
            tree.Add(group, new AccessibleNode(AccessibleRole.ComboBox, "Theme", new Box(120, 130, 120, 20), Value: "Dark"));
        }

        public void Rename(string title)
        {
            Title = title;
            Changed?.Invoke();
        }
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

    [Fact]
    public void Described_chrome_is_read_and_pressed_and_announcements_repeat()
    {
        using var loop = new EpollEventLoop();
        var bus = Run(loop, AccessibilityBus.ForProcessAsync());
        using var tree = CompositorAccessibility.TryStart(loop, "basin-test-compositor", out var reason);
        Assert.True(tree is not null, reason);
        var panel = new Panel();
        tree.Add(panel);
        using var reader = Run(loop, bus.ConnectAsync());
        Run(loop, reader.ListenAsync());

        Run(loop, reader.FindAsync("basin-test-compositor", "frame"));
        var apply = Run(loop, reader.FindAsync("Apply", "push button"));
        Assert.Equal(new AtSpiRect(120, 200, 80, 30), Run(loop, reader.ExtentsAsync(apply)));
        var wrap = Run(loop, reader.FindAsync("Wrap", "check box"));
        Assert.True(Run(loop, reader.HasStateAsync(wrap, AtSpiState.Checked)));
        var theme = Run(loop, reader.FindAsync("Theme", "combo box"));
        Assert.Contains(Run(loop, reader.ChildrenAsync(theme)), n => n.Name == "Dark");

        Assert.True(Run(loop, reader.DoActionAsync(apply, "click")));
        for (var i = 0; i < 100 && panel.Presses == 0; i++)
        {
            loop.Dispatch(10);
        }

        Assert.Equal(1, panel.Presses);

        panel.Rename("Renamed panel");
        Run(loop, reader.FindAsync("Renamed panel"));

        tree.Announce("Workspace 2");
        Run(loop, reader.WaitForAnnouncementAsync("Workspace 2"));
        tree.Announce("Workspace 2");
        Run(loop, reader.WaitForEventAsync(e => e.Member == "Announcement" && e.Text == "Workspace 2 "));
        Assert.Equal("Workspace 2", tree.LastAnnouncement);

        tree.Remove(panel);
        Run(loop, reader.WhereAsync(nodes => !nodes.Any(n => n.Name == "Apply")));
    }
}
