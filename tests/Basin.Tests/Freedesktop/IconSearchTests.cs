using Basin.Freedesktop;
using Xunit;

namespace Basin.Tests;

public sealed class IconSearchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "basin-icons-" + Guid.NewGuid().ToString("N"));

    public IconSearchTests()
    {
        Write("usr/share/icons/breeze/index.theme", """
            [Icon Theme]
            Name=Breeze
            Inherits=paper,hicolor
            Directories=apps/22,apps/48,apps/scalable,ghost/16
            ScaledDirectories=apps/22@2x

            [apps/22]
            Size=22
            Context=Applications
            Type=Fixed

            [apps/22@2x]
            Size=22
            Scale=2
            Type=Fixed

            [apps/48]
            Size=48
            Type=Scalable
            MinSize=48
            MaxSize=256

            [apps/scalable]
            Size=128
            MinSize=8
            MaxSize=512
            Type=Scalable

            [ghost/16]
            Size=16
            """);
        Write("usr/share/icons/breeze/apps/48/x.svg", "svg");
        Write("usr/share/icons/breeze/apps/48/both.svg", "svg");
        Write("usr/share/icons/breeze/apps/48/big.png", "png");
        Write("usr/share/icons/breeze/apps/22/small.png", "png");
        Write("usr/share/icons/breeze/apps/22@2x/small.png", "png");
        Write("usr/share/icons/breeze/apps/scalable/wide.svg", "svg");
        Write("usr/share/icons/paper/index.theme", "[Icon Theme]\nName=Paper\nInherits=breeze\nDirectories=48x48/apps\n\n[48x48/apps]\nSize=48\n");
        Write("usr/share/icons/paper/48x48/apps/p.png", "png");
        Write("usr/share/icons/hicolor/index.theme", "[Icon Theme]\nName=Hicolor\nDirectories=32x32/apps,48x48/apps,scalable/apps\n\n[32x32/apps]\nSize=32\nType=Threshold\n\n[48x48/apps]\nSize=48\nType=Threshold\n\n[scalable/apps]\nSize=128\nMinSize=1\nMaxSize=256\nType=Scalable\n");
        Write("usr/share/icons/hicolor/32x32/apps/y.png", "png");
        Write("usr/share/icons/hicolor/48x48/apps/both.png", "png");
        Write("usr/share/icons/noindex/48x48/apps/bare.png", "png");
        Write("usr/share/icons/noindex/scalable/apps/bare.svg", "svg");
        Write("usr/local/share/icons/breeze/apps/22/local.png", "png");
        Write("usr/share/pixmaps/z.png", "png");
        Write("usr/share/applications/org.kde.konsole.desktop", "[Desktop Entry]\nType=Application\nName=Konsole\nIcon=x\n");
        Write("home/.config/gtk-4.0/settings.ini", "[Settings]\ngtk-icon-theme-name=breeze\n");
        Write("home/.config/gtk-3.0/settings.ini", "[Settings]\ngtk-icon-theme-name=paper\n");
        Write("override/over.png", "png");
    }

    private IReadOnlyList<string> DataDirectories => [Path.Combine(_root, "home/.local/share"), Path.Combine(_root, "usr/local/share"), Path.Combine(_root, "usr/share")];

    private string At(string relative) => Path.Combine(_root, relative);

    private IconSearch Search(string? theme = null) => new()
    {
        DataDirectories = DataDirectories,
        Theme = theme ?? "breeze",
        Entries = new DesktopEntries(DesktopLocale.None, DataDirectories),
    };

    [Fact]
    public void The_theme_chain_reaches_inherits_transitively_then_hicolor_then_pixmaps()
    {
        var search = Search();
        Assert.Equal(At("usr/share/icons/breeze/apps/48/x.svg"), search.Find("x"));
        Assert.Equal(At("usr/share/icons/paper/48x48/apps/p.png"), search.Find("p"));
        Assert.Equal(At("usr/share/icons/hicolor/32x32/apps/y.png"), search.Find("y"));
        Assert.Equal(At("usr/share/pixmaps/z.png"), search.Find("z"));
        Assert.Null(search.Find("nothing"));
    }

    [Fact]
    public void An_explicit_theme_beats_settings_and_hicolor_is_reached_without_inherits()
    {
        var search = Search("hicolor");
        Assert.Null(search.Find("x"));
        Assert.Equal(At("usr/share/icons/hicolor/48x48/apps/both.png"), search.Find("both"));
        Assert.Equal(At("usr/share/icons/breeze/apps/48/both.svg"), Search().Find("both"));
    }

    [Fact]
    public void The_theme_name_comes_from_gtk4_settings_before_gtk3()
    {
        var previous = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", At("home/.config"));
            Assert.Equal("breeze", IconThemeSettings.Read());
            Assert.Equal(At("usr/share/icons/breeze/apps/48/x.svg"), Search(theme: null) is { } s && (s.Theme = null) is null ? s.Find("x") : null);
            File.Delete(At("home/.config/gtk-4.0/settings.ini"));
            Assert.Equal("paper", IconThemeSettings.Read());
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", At("nowhere"));
            Assert.Null(IconThemeSettings.Read());
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previous);
        }
    }

    [Fact]
    public void Sizes_walk_exact_matches_first_then_the_closest_by_distance()
    {
        var pngs = new IconSearch { DataDirectories = DataDirectories, Theme = "breeze", Extensions = [".png"], ReadDesktopEntry = false };
        pngs.Sizes = [22];
        Assert.Equal(At("usr/share/icons/breeze/apps/22/small.png"), pngs.Find("small"));
        pngs.Sizes = [48, 22];
        Assert.Equal(At("usr/share/icons/breeze/apps/22/small.png"), pngs.Find("small"));
        pngs.Sizes = [16];
        Assert.Equal(At("usr/share/icons/breeze/apps/22/small.png"), pngs.Find("small"));
        Assert.Equal(At("usr/share/icons/breeze/apps/48/big.png"), pngs.Find("big"));
        pngs.Sizes = [512];
        Assert.Equal(At("usr/share/icons/breeze/apps/48/big.png"), pngs.Find("big"));
        var svgs = new IconSearch { DataDirectories = DataDirectories, Theme = "breeze", Sizes = [512], ReadDesktopEntry = false };
        Assert.Equal(At("usr/share/icons/breeze/apps/scalable/wide.svg"), svgs.Find("wide"));
        Assert.Null(pngs.Find("wide"));
    }

    [Fact]
    public void A_scaled_directory_is_not_matched_at_scale_one_and_a_theme_spans_data_directories()
    {
        var pngs = new IconSearch { DataDirectories = DataDirectories, Theme = "breeze", Extensions = [".png"], ReadDesktopEntry = false, Sizes = [22] };
        Assert.Equal(At("usr/share/icons/breeze/apps/22/small.png"), pngs.Find("small"));
        Assert.Equal(At("usr/local/share/icons/breeze/apps/22/local.png"), pngs.Find("local"));
        var theme = IconTheme.Load("breeze", DataDirectories)!;
        Assert.Equal(["paper", "hicolor"], theme.Inherits);
        Assert.Equal(2, theme.BaseDirectories.Count);
        Assert.DoesNotContain(theme.Directories, d => d.Name == "ghost/16");
        Assert.Contains(theme.Directories, d => d.Name == "apps/22@2x" && d.Scale == 2);
    }

    [Fact]
    public void Scale_two_asks_for_the_2x_directory_and_falls_to_the_closest_by_scaled_distance()
    {
        var pngs = new IconSearch { DataDirectories = DataDirectories, Theme = "breeze", Extensions = [".png"], ReadDesktopEntry = false, Sizes = [22], Scale = 2 };
        Assert.Equal(At("usr/share/icons/breeze/apps/22@2x/small.png"), pngs.Find("small"));
        Assert.Equal(At("usr/share/icons/breeze/apps/48/big.png"), pngs.Find("big"));
        pngs.Scale = 1;
        Assert.Equal(At("usr/share/icons/breeze/apps/22/small.png"), pngs.Find("small"));
        var hicolor = new IconSearch { DataDirectories = DataDirectories, Theme = "hicolor", Extensions = [".png"], ReadDesktopEntry = false, Sizes = [32], Scale = 2 };
        Assert.Equal(At("usr/share/icons/hicolor/32x32/apps/y.png"), hicolor.Find("y"));
    }

    [Fact]
    public void A_theme_without_an_index_is_walked_by_the_fixed_layout()
    {
        var search = new IconSearch { DataDirectories = DataDirectories, Theme = "noindex", ReadDesktopEntry = false };
        Assert.Equal(At("usr/share/icons/noindex/scalable/apps/bare.svg"), search.Find("bare"));
        search.Extensions = [".png"];
        Assert.Equal(At("usr/share/icons/noindex/48x48/apps/bare.png"), search.Find("bare"));
        Assert.False(IconTheme.Load("noindex", DataDirectories)!.HasIndex);
        Assert.Null(IconTheme.Load("absent", DataDirectories));
    }

    [Fact]
    public void The_override_directory_wins_and_the_icon_name_comes_through_the_app_id_ladder()
    {
        var search = Search();
        search.OverrideDirectory = At("override");
        Assert.Equal(At("override/over.png"), search.Find("over"));
        Assert.Equal(At("usr/share/icons/breeze/apps/48/x.svg"), search.Find("org.kde.konsole"));
        Assert.Equal(At("usr/share/icons/breeze/apps/48/x.svg"), search.Find("ORG.KDE.KONSOLE"));
        Assert.Null(search.Find("konsole"));
    }

    [Fact]
    public void Invalidate_rereads_the_theme_so_a_new_icon_appears()
    {
        var search = Search();
        Assert.Null(search.Find("late"));
        Write("usr/share/icons/breeze/apps/48/late.svg", "svg");
        Assert.Equal(At("usr/share/icons/breeze/apps/48/late.svg"), search.Find("late"));
        Write("usr/share/icons/breeze/apps/22/late2.png", "png");
        Directory.CreateDirectory(At("usr/share/icons/breeze/apps/64"));
        Assert.Contains("apps/22", search.Find("late2") ?? "");
        search.Invalidate();
        Assert.Equal(At("usr/share/icons/breeze/apps/48/late.svg"), search.Find("late"));
    }

    [Fact]
    public void Config_directories_follow_the_same_rules_as_data_directories()
    {
        var previous = (Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"), Environment.GetEnvironmentVariable("XDG_CONFIG_DIRS"));
        try
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", "/tmp/cfg");
            Environment.SetEnvironmentVariable("XDG_CONFIG_DIRS", "/etc/one::relative:/etc/two");
            Assert.Equal("/tmp/cfg", XdgDirectories.ConfigHome);
            Assert.Equal(["/tmp/cfg", "/etc/one", "/etc/two"], XdgDirectories.ConfigDirectories());
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", "relative");
            Environment.SetEnvironmentVariable("XDG_CONFIG_DIRS", null);
            Assert.EndsWith(".config", XdgDirectories.ConfigHome);
            Assert.Equal([XdgDirectories.ConfigHome, "/etc/xdg"], XdgDirectories.ConfigDirectories());
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previous.Item1);
            Environment.SetEnvironmentVariable("XDG_CONFIG_DIRS", previous.Item2);
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
