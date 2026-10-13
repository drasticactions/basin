using System.Globalization;
using Basin.Capabilities;
using Xunit;

namespace Basin.Tests;

public sealed class ProcessTreeTests
{
    [Fact]
    public void A_grandchild_belongs_to_the_tree_of_the_process_that_spawned_it()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the walk reads /proc");

        var marker = Path.Combine(Path.GetTempPath(), $"basin-owns-{Environment.ProcessId}-{Environment.TickCount64}");
        using var host = new CompositorTestHost();
        try
        {
            using var tracker = new ProcessTracker(host.Loop, () => ProcessTracker.SessionEnvironment("wayland-not-connected", null, null));
            var child = tracker.SpawnShell($"sh -c 'sleep 300 & echo $$ $! > \"{marker}.tmp\"; mv \"{marker}.tmp\" \"{marker}\"; wait'; true", out var error);
            Assert.True(child is not null, error);

            var pids = WaitForPids(marker);
            Assert.Equal(2, pids.Length);
            var (middle, leaf) = (pids[0], pids[1]);

            Assert.Equal(child!.Pid, ProcessIdentity.ParentOf(middle));
            Assert.Equal(middle, ProcessIdentity.ParentOf(leaf));
            Assert.True(tracker.OwnsTree(child.Pid));
            Assert.True(tracker.OwnsTree(middle));
            Assert.True(tracker.OwnsTree(leaf));
            Assert.False(tracker.OwnsTree(Environment.ProcessId));
            Assert.False(tracker.OwnsTree(1));
            Assert.False(tracker.Owns(leaf));

            tracker.TerminateAll(TimeSpan.FromSeconds(2));
            Assert.False(tracker.OwnsTree(child.Pid));
        }
        finally
        {
            File.Delete(marker);
        }
    }

    [Fact]
    public void The_parent_of_this_process_is_read_from_proc()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the parent is read from /proc");

        Assert.True(ProcessIdentity.ParentOf(Environment.ProcessId) > 0);
        Assert.Equal(0, ProcessIdentity.ParentOf(0));
        Assert.Equal(0, ProcessIdentity.ParentOf(-1));
        Assert.Equal(0, ProcessIdentity.ParentOf(int.MaxValue));
    }

    [Theory]
    [InlineData("us", "us", null)]
    [InlineData("us(intl)", "us", "intl")]
    [InlineData("de ( nodeadkeys )", "de", "nodeadkeys")]
    [InlineData("(intl)", "(intl)", null)]
    [InlineData("fr(", "fr(", null)]
    public void A_layout_id_splits_into_layout_and_variant(string id, string layout, string? variant)
    {
        var names = KeymapNames.Parse(id);

        Assert.Equal(layout, names.Layout);
        Assert.Equal(variant, names.Variant);
        Assert.Null(names.Rules);
        Assert.Null(names.Options);
    }

    [Fact]
    public void The_environment_lines_name_only_what_is_set()
    {
        Assert.Equal(["XKB_DEFAULT_LAYOUT=us"], KeymapNames.Parse("us").EnvironmentLines());
        Assert.Equal(["XKB_DEFAULT_LAYOUT=us", "XKB_DEFAULT_VARIANT=intl"], KeymapNames.Parse("us(intl)").EnvironmentLines());
        Assert.Empty(new KeymapNames().EnvironmentLines());
        Assert.Equal(
            ["XKB_DEFAULT_RULES=evdev", "XKB_DEFAULT_LAYOUT=de", "XKB_DEFAULT_OPTIONS=caps:escape"],
            new KeymapNames(Rules: "evdev", Layout: "de", Options: "caps:escape").EnvironmentLines());
    }

    private static int[] WaitForPids(string marker)
    {
        for (var i = 0; i < 250; i++)
        {
            if (File.Exists(marker))
            {
                return [.. File.ReadAllText(marker).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(text => int.Parse(text, CultureInfo.InvariantCulture))];
            }

            Thread.Sleep(20);
        }

        return [];
    }
}
