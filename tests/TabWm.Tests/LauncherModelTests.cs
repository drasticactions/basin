using Xunit;

namespace TabWm.Tests;

public sealed class LauncherModelTests
{
    private static LauncherModel Model(params string[] pinned)
    {
        var model = new LauncherModel(pinned);
        model.Load(
        [
            new LauncherItem("foot.desktop", "Foot", "Terminal", "foot", ["shell", "prompt"], "foot", "System"),
            new LauncherItem("org.kde.konsole.desktop", "Konsole", "Terminal", "utilities-terminal", ["cmd"], "konsole", "System"),
            new LauncherItem("firefox.desktop", "Firefox", "Web Browser", "firefox", ["internet"], "firefox", "Internet"),
            new LauncherItem("org.gnome.Calculator.desktop", "Calculator", null, "accessories-calculator", ["math"], "gnome-calculator", "Accessories"),
        ]);
        return model;
    }

    [Fact]
    public void A_name_prefix_ranks_before_a_generic_name_match()
    {
        var results = Model().Search("term");
        Assert.Equal(["Foot", "Konsole"], results.Select(r => r.Name));
        Assert.Equal("Konsole", Model().Search("kon")[0].Name);
    }

    [Fact]
    public void Search_reaches_keywords_and_the_exec_basename()
    {
        Assert.Equal("Calculator", Assert.Single(Model().Search("math")).Name);
        Assert.Equal("Calculator", Assert.Single(Model().Search("gnome-calc")).Name);
        Assert.Empty(Model().Search("zzz"));
        Assert.Empty(Model().Search("   "));
    }

    [Fact]
    public void Pinned_follows_the_seed_and_skips_entries_that_are_not_installed()
    {
        var model = Model("firefox.desktop", "missing.desktop", "foot.desktop");
        Assert.Equal(["Firefox", "Foot"], model.PinnedItems().Select(item => item.Name));
    }

    [Fact]
    public void Pin_unpin_and_reorder_change_the_pinned_row()
    {
        var model = Model("foot.desktop");
        var changes = 0;
        model.Changed += () => changes++;
        Assert.True(model.Pin("firefox.desktop"));
        Assert.False(model.Pin("firefox.desktop"));
        Assert.True(model.MovePinned("firefox.desktop", 0));
        Assert.Equal(["firefox.desktop", "foot.desktop"], model.Pinned);
        Assert.True(model.Unpin("foot.desktop"));
        Assert.Equal(["firefox.desktop"], model.Pinned);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void Categories_group_and_sort_the_entries()
    {
        var categories = Model().Categories();
        Assert.Equal(["Accessories", "Internet", "System"], categories.Select(c => c.Category));
        Assert.Equal(2, categories[2].Items.Count);
    }
}
