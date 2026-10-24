using Xunit;

namespace LunaWm.Tests;

public sealed class LauncherStateTests
{
    private static LauncherEntry Entry(string id, string name, params string[] categories) => new(id, name, null, categories, null, []);

    private static readonly LauncherTab[] Seed =
    [
        new("Apps", [], []),
        new("Favorites", [], ["fav.desktop"]),
        new("Settings", ["Settings", "System"], []),
    ];

    private static readonly LauncherEntry[] Entries =
    [
        Entry("zed.desktop", "Zed", "Development"),
        Entry("fav.desktop", "Fav", "Network"),
        Entry("ctl.desktop", "Control", "Settings"),
        Entry("abc.desktop", "Abc", "Office"),
    ];

    [Fact]
    public void The_seed_places_named_apps_then_categories_then_the_rest_alphabetically()
    {
        var state = LauncherState.Build(Seed, ["fav.desktop", "missing.desktop"], Entries, null);
        Assert.Equal(["abc.desktop", "zed.desktop"], state.Tabs[0].Apps);
        Assert.Equal(["fav.desktop"], state.Tabs[1].Apps);
        Assert.Equal(["ctl.desktop"], state.Tabs[2].Apps);
        Assert.Equal(["fav.desktop"], state.QuickLaunch);
    }

    [Fact]
    public void The_state_file_wins_for_every_entry_it_names_and_new_entries_follow_the_seed()
    {
        var saved = new LauncherFile
        {
            Tabs = [new LauncherFileTab { Name = "Settings", Apps = ["zed.desktop"] }, new LauncherFileTab { Name = "Apps", Apps = ["fav.desktop", "gone.desktop"] }],
            Hidden = ["abc.desktop"],
            QuickLaunch = ["zed.desktop"],
        };
        var state = LauncherState.Build(Seed, ["fav.desktop"], Entries, saved);
        Assert.Equal(["fav.desktop", "abc.desktop"], state.Tabs[0].Apps);
        Assert.Empty(state.Tabs[1].Apps);
        Assert.Equal(["zed.desktop", "ctl.desktop"], state.Tabs[2].Apps);
        Assert.Contains("abc.desktop", state.Hidden);
        Assert.Equal(["zed.desktop"], state.QuickLaunch);
    }

    [Fact]
    public void A_removed_entry_drops_out_and_a_round_trip_keeps_the_order()
    {
        var state = LauncherState.Build(Seed, [], Entries, null);
        state.Move("zed.desktop", "Favorites", 0);
        state.Hide("ctl.desktop");
        state.SetQuickLaunch(0, "abc.desktop");
        var path = Path.Combine(Path.GetTempPath(), $"luna-wm-launcher-{Environment.ProcessId}-{Guid.NewGuid():N}.json");
        try
        {
            Assert.True(state.Write(path, out var error), error?.Message);
            var file = LauncherState.Read(path, out error);
            Assert.Null(error);
            var again = LauncherState.Build(Seed, [], Entries.Where(entry => entry.Id != "fav.desktop").ToArray(), file);
            Assert.Equal(["zed.desktop"], again.Tabs[1].Apps);
            Assert.Equal(["abc.desktop"], again.Tabs[0].Apps);
            Assert.Contains("ctl.desktop", again.Hidden);
            Assert.Equal(["abc.desktop"], again.QuickLaunch);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void An_unreadable_state_file_reports_and_the_dock_holds_six()
    {
        var path = Path.Combine(Path.GetTempPath(), $"luna-wm-launcher-{Environment.ProcessId}-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ not json");
        try
        {
            Assert.Null(LauncherState.Read(path, out var error));
            Assert.NotNull(error);
        }
        finally
        {
            File.Delete(path);
        }

        var many = Enumerable.Range(0, 9).Select(i => Entry($"e{i}.desktop", $"E{i}")).ToArray();
        var state = LauncherState.Build(Seed, [.. many.Select(entry => entry.Id)], many, null);
        Assert.Equal(6, state.QuickLaunch.Count);
        state.SetQuickLaunch(9, "e8.desktop");
        Assert.Equal(6, state.QuickLaunch.Count);
        state.SetQuickLaunch(2, "e8.desktop");
        Assert.Equal("e8.desktop", state.QuickLaunch[2]);
    }
}
