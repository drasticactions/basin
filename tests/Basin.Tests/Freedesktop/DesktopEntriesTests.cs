using Basin.Freedesktop;
using Xunit;

namespace Basin.Tests;

public sealed class DesktopEntriesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "basin-freedesktop-" + Guid.NewGuid().ToString("N"));

    public DesktopEntriesTests()
    {
        Write("home/.local/share/applications/user.desktop", "[Desktop Entry]\nType=Application\nName=User Copy\nIcon=user-icon\nExec=user\n");
        Write("home/.local/share/applications/hidden-override.desktop", "[Desktop Entry]\nType=Application\nName=Hidden\nHidden=true\n");
        Write("usr/share/applications/user.desktop", "[Desktop Entry]\nType=Application\nName=System Copy\nExec=system\n");
        Write("usr/share/applications/hidden-override.desktop", "[Desktop Entry]\nType=Application\nName=Must Not Appear\n");
        Write("usr/share/applications/org.gnome.Nautilus.desktop", "# a comment\n[Desktop Entry]\nName[ja]=ファイル\nType=Application\nName=Files\nName[ja_JP]=ファイル (JP)\nIcon=org.gnome.Nautilus\nStartupWMClass=nautilus\nExec=nautilus %U\n[Desktop Action new-window]\nName=New Window\n");
        Write("usr/share/applications/kde4/kate.desktop", "[Desktop Entry]\nType=Application\nName=Kate (KDE 4)\n");
        Write("usr/share/applications/kate.desktop", "[Desktop Entry]\nType=Application\nName=Kate\nIcon=kate\n");
        Write("usr/share/applications/com.obsproject.Studio.desktop", "[Desktop Entry]\nType=Application\nName=OBS\\sStudio\nIcon=com.obsproject.Studio\n");
        Write("usr/share/applications/my_app-thing.desktop", "[Desktop Entry]\nType=Application\nName=My App Thing\n");
        Write("usr/share/applications/malformed.desktop", "[Desktop Entry]\nType=Application\nthis line has no equals\nName=Malformed But Named\n");
        Write("usr/share/applications/nameless.desktop", "[Desktop Entry]\nType=Application\nIcon=nothing\n");
        Write("usr/share/applications/nodisplay.desktop", "[Desktop Entry]\nType=Application\nName=Background Helper\nExec=helper\nNoDisplay=true\n");
        Write("usr/share/applications/link.desktop", "[Desktop Entry]\nType=Link\nName=A Link\nURL=https://example.org\n");
        Write("usr/share/applications/terminal.desktop", "[Desktop Entry]\nType=Application\nName=Top\nExec=htop\nTerminal=true\nCategories=System;Monitor;\n");
        Write("usr/share/applications/tryexec-missing.desktop", "[Desktop Entry]\nType=Application\nName=Gone\nExec=gone\nTryExec=/nonexistent/gone\n");
        Write("usr/share/applications/onlyshowin.desktop", "[Desktop Entry]\nType=Application\nName=Gnome Only\nExec=gnome-thing\nOnlyShowIn=GNOME;\n");
        Write("usr/share/icons/hicolor/48x48/apps/org.gnome.Nautilus.png", "png");
        Write("usr/share/pixmaps/kate.png", "png");
    }

    private IReadOnlyList<string> DataDirectories => [Path.Combine(_root, "home/.local/share"), Path.Combine(_root, "usr/share")];

    private DesktopEntries Entries(DesktopLocale? locale = null) => new(locale ?? DesktopLocale.None, DataDirectories);

    [Fact]
    public void The_user_directory_shadows_the_system_one_and_a_hidden_entry_deletes_it()
    {
        var entries = Entries();
        var ids = entries.All().Select(e => e.Id).ToList();
        Assert.Contains("user.desktop", ids);
        Assert.DoesNotContain("hidden-override.desktop", ids);
        Assert.Equal("User Copy", entries.Find("user")?.Name);
        Assert.Null(entries.Find("hidden-override"));
        Assert.Contains("kde4-kate.desktop", ids);
        Assert.DoesNotContain("nameless.desktop", ids);
        Assert.Equal("Malformed But Named", entries.Find("malformed.desktop")?.Name);
        Assert.Equal("OBS Studio", entries.Find("com.obsproject.Studio")?.Name);
        Assert.Equal(ids.OrderBy(id => entries.Find(id)!.Name, StringComparer.OrdinalIgnoreCase), ids);
    }

    [Fact]
    public void Listable_filters_what_a_menu_must_not_show_and_lookup_does_not()
    {
        var entries = Entries();
        var listed = entries.Listable().Select(e => e.Id).ToList();
        Assert.Equal(["org.gnome.Nautilus.desktop", "terminal.desktop", "user.desktop"], listed);
        Assert.Equal("Background Helper", entries.FindForAppId("nodisplay")?.Name);
        Assert.Equal("A Link", entries.Find("link")?.Name);
        Assert.Contains("onlyshowin.desktop", entries.Listable(new HashSet<string> { "GNOME" }).Select(e => e.Id));
        Assert.True(entries.Find("terminal")!.Terminal);
        Assert.Equal(DesktopMainCategory.System, DesktopCategories.MainOf(entries.Find("terminal")!));
        Assert.Equal(["nautilus"], entries.Find("org.gnome.Nautilus")!.Argv);
    }

    [Fact]
    public void Find_before_a_scan_reads_the_one_file_and_honours_the_user_directory()
    {
        var entries = Entries();
        Assert.Equal("User Copy", entries.Find("user")?.Name);
        Assert.Null(entries.Find("hidden-override"));
        Assert.Null(entries.Find("missing"));
    }

    [Fact]
    public void The_localised_name_wins_in_the_order_the_spec_gives_whatever_the_file_order()
    {
        Assert.Equal("Files", Entries().Find("org.gnome.Nautilus")?.Name);
        Assert.Equal("ファイル", Entries(DesktopLocale.Parse("ja")).Find("org.gnome.Nautilus")?.Name);
        Assert.Equal("ファイル (JP)", Entries(DesktopLocale.Parse("ja_JP.UTF-8")).Find("org.gnome.Nautilus")?.Name);
        Assert.Equal("Files", Entries(DesktopLocale.Parse("de_DE")).Find("org.gnome.Nautilus")?.Name);
        Assert.Equal("Files", Entries(DesktopLocale.Parse("C")).Find("org.gnome.Nautilus")?.Name);
    }

    [Fact]
    public void Locale_parse_splits_lang_country_encoding_and_modifier()
    {
        Assert.Equal(new DesktopLocale("ja", "JP", "mod"), DesktopLocale.Parse("ja_JP.UTF-8@mod"));
        Assert.Equal(new DesktopLocale("en", null, null), DesktopLocale.Parse("en"));
        Assert.True(DesktopLocale.Parse("POSIX").IsNone);
        Assert.True(DesktopLocale.None.IsNone);
    }

    [Fact]
    public void The_app_id_ladder_reaches_each_rung()
    {
        var entries = Entries();
        Assert.Equal("org.gnome.Nautilus.desktop", entries.FindForAppId("org.gnome.Nautilus")?.Id);
        Assert.Equal("org.gnome.Nautilus.desktop", entries.FindForAppId("ORG.GNOME.NAUTILUS")?.Id);
        Assert.Equal("org.gnome.Nautilus.desktop", entries.FindForAppId("nautilus")?.Id);
        Assert.Equal("kate.desktop", entries.FindForAppId("org.kde.kate")?.Id);
        Assert.Equal("kde4-kate.desktop", entries.FindForAppId("kde4-kate")?.Id);
        Assert.Equal("my_app-thing.desktop", entries.FindForAppId("myappthing")?.Id);
        Assert.Null(entries.FindForAppId("nothing-here"));
        Assert.Null(entries.FindForAppId(""));
        Assert.Same(entries.FindForAppId("nautilus"), entries.FindForAppId("nautilus"));
    }

    [Fact]
    public void Invalidate_drops_the_cache_so_a_new_file_appears()
    {
        var entries = Entries();
        Assert.Null(entries.FindForAppId("late"));
        Write("usr/share/applications/late.desktop", "[Desktop Entry]\nType=Application\nName=Late\n");
        Assert.Null(entries.FindForAppId("late"));
        entries.Invalidate();
        Assert.Equal("Late", entries.FindForAppId("late")?.Name);
    }

    [Fact]
    public void Icon_search_reads_the_icon_name_through_the_app_id_ladder()
    {
        var search = new IconSearch { Entries = Entries(), Extensions = [".png"] };
        InFixtureEnvironment(() =>
        {
            Assert.Equal(DataDirectories, XdgDirectories.DataDirectories());
            Assert.Equal(Path.Combine(_root, "usr/share/icons/hicolor/48x48/apps/org.gnome.Nautilus.png"), search.Find("nautilus"));
            Assert.Equal(Path.Combine(_root, "usr/share/pixmaps/kate.png"), search.Find("org.kde.kate"));
            Assert.Null(search.Find("com.obsproject.Studio"));
        });
    }

    [Fact]
    public void The_portal_resolver_names_what_it_can_and_declines_the_rest()
    {
        var resolver = new BasinPortal.DesktopAppInfoResolver(Entries(), new IconSearch { Extensions = [".png"] });
        InFixtureEnvironment(() =>
        {
            Assert.False(resolver.TryResolve("", out _));
            Assert.False(resolver.TryResolve("nothing-here", out _));
            Assert.True(resolver.TryResolve("com.obsproject.Studio", out var obs));
            Assert.Equal(new Capabilities.AppInfo("OBS Studio", ""), obs);
            Assert.True(resolver.TryResolve("kate", out var kate));
            Assert.Equal(("Kate", Path.Combine(_root, "usr/share/pixmaps/kate.png")), (kate.DisplayName, kate.IconPath));
        });
    }

    private void InFixtureEnvironment(Action body)
    {
        var previous = (Environment.GetEnvironmentVariable("XDG_DATA_HOME"), Environment.GetEnvironmentVariable("XDG_DATA_DIRS"));
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", DataDirectories[0]);
        Environment.SetEnvironmentVariable("XDG_DATA_DIRS", DataDirectories[1]);
        try
        {
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", previous.Item1);
            Environment.SetEnvironmentVariable("XDG_DATA_DIRS", previous.Item2);
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
