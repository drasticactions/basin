using Basin.Freedesktop;
using Xunit;

namespace Basin.Tests;

public sealed class DesktopCategoriesTests
{
    [Fact]
    public void The_first_registered_main_category_wins_and_audio_or_video_fold_into_audiovideo()
    {
        Assert.Equal(DesktopMainCategory.Utility, DesktopCategories.MainOf(Entry("a", "TextEditor", "Utility", "Development")));
        Assert.Equal(DesktopMainCategory.AudioVideo, DesktopCategories.MainOf(Entry("b", "Audio", "AudioVideo")));
        Assert.Equal(DesktopMainCategory.AudioVideo, DesktopCategories.MainOf(Entry("c", "Player", "Video")));
        Assert.Equal(DesktopMainCategory.Other, DesktopCategories.MainOf(Entry("d", "TextEditor")));
        Assert.Equal(DesktopMainCategory.Other, DesktopCategories.MainOf(Entry("e")));
        Assert.Null(DesktopCategories.Registered("TextEditor"));
        Assert.Equal(DesktopMainCategory.Game, DesktopCategories.Registered("Game"));
    }

    [Fact]
    public void Default_labels_are_the_menu_spec_default_layout_names()
    {
        Assert.Equal("Sound & Video", DesktopCategories.DefaultLabel(DesktopMainCategory.AudioVideo));
        Assert.Equal("Programming", DesktopCategories.DefaultLabel(DesktopMainCategory.Development));
        Assert.Equal("Internet", DesktopCategories.DefaultLabel(DesktopMainCategory.Network));
        Assert.Equal("System Tools", DesktopCategories.DefaultLabel(DesktopMainCategory.System));
        Assert.Equal("Accessories", DesktopCategories.DefaultLabel(DesktopMainCategory.Utility));
        Assert.Equal("Other", DesktopCategories.DefaultLabel(DesktopMainCategory.Other));
        foreach (var category in Enum.GetValues<DesktopMainCategory>())
        {
            Assert.NotEmpty(DesktopCategories.DefaultLabel(category));
        }
    }

    [Fact]
    public void Group_orders_by_the_registered_list_keeps_input_order_within_and_omits_empty_groups()
    {
        var game = Entry("game", "Game", "ArcadeGame");
        var editor = Entry("editor", "Utility", "TextEditor");
        var ide = Entry("ide", "Development", "IDE");
        var odd = Entry("odd", "Bespoke");
        var tool = Entry("tool", "Utility");
        var groups = DesktopCategories.Group([tool, odd, game, editor, ide]);
        Assert.Equal([DesktopMainCategory.Development, DesktopMainCategory.Game, DesktopMainCategory.Utility, DesktopMainCategory.Other], groups.Select(g => g.Category));
        Assert.Equal([tool, editor], groups[2].Entries);
        Assert.Equal([odd], groups[3].Entries);
        Assert.Empty(DesktopCategories.Group([]));
    }

    private static DesktopEntry Entry(string id, params string[] categories) => new()
    {
        Id = id + ".desktop",
        Path = "/" + id,
        Name = id,
        Type = DesktopEntryType.Application,
        Categories = categories,
    };
}
