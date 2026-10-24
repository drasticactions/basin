using AvaUnion.Engine;
using Basin.Config;
using Basin.Diagnostics;
using Xunit;

namespace TabWm.Tests;

public sealed class ConfigTests
{
    private static readonly BasinLogger Log = BasinLog.For("tab-wm-tests");

    [Fact]
    public void The_embedded_template_reads_to_the_documented_defaults()
    {
        using var reader = new StreamReader(TabWmConfig.TemplateStream());
        var config = TabWmConfig.FromText(reader.ReadToEnd(), Log);
        Assert.Equal(UnionEngineKind.Auto, config.Engine);
        Assert.Equal(5000, config.CloseTimeoutMs);
        Assert.Contains("foot.desktop", config.Pinned);
        Assert.True(config.Restore);
        Assert.Contains(config.Bindings, b => b.Action == TabAction.NewTab);
    }

    [Fact]
    public void Keys_rebind_unbind_and_exec()
    {
        var config = TabWmConfig.FromText(
            """
            [theme]
            engine = "managed"
            [tabs]
            close-when-empty = true
            [keys]
            "Super+T" = false
            "Super+N" = "new-tab"
            "Super+Return" = ["foot", "-e", "htop"]
            """,
            Log);
        Assert.Equal(UnionEngineKind.Managed, config.Engine);
        Assert.True(config.CloseWhenEmpty);
        Assert.True(HotkeyParser.TryParseChord("Super+T", Log, out var t, out var tMods));
        Assert.DoesNotContain(config.Bindings, b => b.Keysym == Lower(t) && b.Modifiers == tMods);
        Assert.True(HotkeyParser.TryParseChord("Super+N", Log, out var n, out var nMods));
        Assert.Contains(config.Bindings, b => b.Keysym == Lower(n) && b.Modifiers == nMods && b.Action == TabAction.NewTab);
        var exec = Assert.Single(config.Bindings, b => b.Action == TabAction.Exec);
        Assert.Equal(["foot", "-e", "htop"], exec.Command!);
    }

    private static uint Lower(uint keysym) => new Xkb.XkbKeysym(keysym).ToLower().Value;

    [Theory]
    [InlineData("tab-3", "SelectTab", 3)]
    [InlineData("move-to-tab-9", "MoveWindowToTab", 9)]
    [InlineData("move-tab-right", "MoveTabRight", 0)]
    public void Action_names_parse(string name, string action, int argument) =>
        Assert.Equal((Enum.Parse<TabAction>(action), argument), TabBindings.Parse(name));

    [Fact]
    public void Unknown_action_names_do_not_parse()
    {
        Assert.Null(TabBindings.Parse("tab-0"));
        Assert.Null(TabBindings.Parse("move-to-tab-10"));
        Assert.Null(TabBindings.Parse("explode"));
    }
}
