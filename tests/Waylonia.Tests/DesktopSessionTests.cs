using Waylonia;
using Xunit;

namespace Waylonia.Tests;

public sealed class DesktopSessionTests
{
    [Theory]
    [InlineData("sway", "sway")]
    [InlineData("niri", "niri")]
    [InlineData("plasma", "KDE")]
    [InlineData("cosmic", "COSMIC")]
    [InlineData("xfce", "XFCE")]
    public void Every_built_in_recipe_names_the_desktop_it_starts(string name, string currentDesktop)
    {
        var recipe = Assert.IsType<DesktopRecipe>(DesktopRecipes.Find(name));

        Assert.Equal(currentDesktop, recipe.CurrentDesktop);
        Assert.NotEqual(0, recipe.Command.Length);
        Assert.True(recipe.Bus);
    }

    [Fact]
    public void A_recipe_name_is_matched_without_regard_to_case()
    {
        Assert.NotNull(DesktopRecipes.Find("Plasma"));
        Assert.Null(DesktopRecipes.Find("enlightenment"));
    }

    [Fact]
    public void Only_the_wlroots_recipe_falls_back_to_software()
    {
        Assert.True(DesktopRecipes.Find("sway")!.SoftwareFallback);
        Assert.False(DesktopRecipes.Find("niri")!.SoftwareFallback);
    }

    [Fact]
    public void A_wlroots_guest_on_a_channel_with_no_gpu_gets_the_software_pair()
    {
        var recipe = DesktopRecipes.Find("sway")!;

        var withoutGpu = DesktopSession.Environment(recipe, [], gpu: false);
        Assert.Contains("WLR_RENDERER=pixman", withoutGpu);
        Assert.Contains("WLR_NO_HARDWARE_CURSORS=1", withoutGpu);

        var withGpu = DesktopSession.Environment(recipe, [], gpu: true);
        Assert.DoesNotContain("WLR_RENDERER=pixman", withGpu);
    }

    [Fact]
    public void A_smithay_guest_never_gets_the_wlroots_variables()
    {
        var environment = DesktopSession.Environment(
            DesktopRecipes.Find("niri")!, [], gpu: false);

        Assert.DoesNotContain("WLR_RENDERER=pixman", environment);
        Assert.DoesNotContain("WLR_NO_HARDWARE_CURSORS=1", environment);
    }


    [Fact]
    public void A_profile_assignment_comes_after_the_recipe_s_own()
    {
        var environment = DesktopSession.Environment(
            DesktopRecipes.Find("plasma")!, ["QT_QPA_PLATFORM=wayland"], gpu: true);

        Assert.Equal("XDG_SESSION_DESKTOP=KDE", environment[0]);
        Assert.Equal("QT_QPA_PLATFORM=wayland", environment[^1]);
    }

    [Fact]
    public void The_wrapper_waits_for_the_socket_before_it_starts_anything()
    {
        var wrapper = Wrapper("plasma");

        Assert.StartsWith("d=\"$XDG_RUNTIME_DIR/waylonia-7\"", wrapper, StringComparison.Ordinal);
        Assert.Contains("while [ ! -S \"$d\" ]", wrapper, StringComparison.Ordinal);
    }

    [Fact]
    public void The_wrapper_exports_the_six_variables_a_desktop_reads()
    {
        var wrapper = Wrapper("plasma");

        Assert.Contains("WAYLAND_DISPLAY=waylonia-7", wrapper, StringComparison.Ordinal);
        Assert.Contains("XDG_SESSION_TYPE=wayland", wrapper, StringComparison.Ordinal);
        Assert.Contains("XDG_SESSION_CLASS=user", wrapper, StringComparison.Ordinal);
        Assert.Contains("XDG_CURRENT_DESKTOP=KDE", wrapper, StringComparison.Ordinal);
        Assert.Contains("XDG_SESSION_DESKTOP=plasma", wrapper, StringComparison.Ordinal);
        Assert.Contains("DESKTOP_SESSION=plasma", wrapper, StringComparison.Ordinal);
    }

    [Fact]
    public void The_wrapper_runs_the_bus_and_exports_the_display_into_it()
    {
        var wrapper = Wrapper("plasma");

        Assert.Contains("exec dbus-run-session --", wrapper, StringComparison.Ordinal);
        Assert.Contains("dbus-update-activation-environment --systemd", wrapper, StringComparison.Ordinal);
        Assert.True(
            wrapper.IndexOf("dbus-run-session", StringComparison.Ordinal)
                < wrapper.IndexOf("dbus-update-activation-environment", StringComparison.Ordinal),
            "the activation environment is exported outside the bus it belongs to");
    }

    [Fact]
    public void The_command_is_reached_through_env_rather_than_a_bare_prefix()
    {
        var wrapper = DesktopSession.Wrapper(
            DesktopRecipes.Find("plasma")!,
            "waylonia-7",
            "startplasma-wayland",
            ["QT_QPA_PLATFORM=wayland"]);

        Assert.Contains(
            "exec env QT_QPA_PLATFORM=wayland startplasma-wayland",
            wrapper.Replace("XDG_SESSION_DESKTOP=KDE ", string.Empty, StringComparison.Ordinal),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_wrapper_unsets_the_host_s_own_x_display()
    {
        Assert.Contains("unset DISPLAY", Wrapper("plasma"), StringComparison.Ordinal);
        Assert.DoesNotContain("export DISPLAY", Wrapper("plasma"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1920x1080", 1920, 1080)]
    [InlineData("800X600", 800, 600)]
    public void A_desktop_size_is_read_as_width_by_height(string text, int width, int height)
    {
        Assert.Equal((width, height), Program.ParseSize(text));
    }

    [Theory]
    [InlineData("1920")]
    [InlineData("1920x")]
    [InlineData("0x1080")]
    [InlineData("wide")]
    public void A_size_that_is_not_width_by_height_is_refused(string text)
    {
        Assert.Null(Program.ParseSize(text));
    }

    private static string Wrapper(string name)
    {
        var recipe = DesktopRecipes.Find(name)!;
        return DesktopSession.Wrapper(
            recipe,
            "waylonia-7",
            recipe.Command,
            DesktopSession.Environment(recipe, [], gpu: true));
    }
}
