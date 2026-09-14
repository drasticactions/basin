using Basin.Freedesktop;
using Xunit;

namespace Basin.Tests;

public sealed class DesktopEntryReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "basin-freedesktop-reader-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Every_key_of_the_spec_is_read()
    {
        var entry = Parse("full.desktop", """
            [Desktop Entry]
            Type=Application
            Version=1.5
            Name=Full
            Name[de]=Voll
            GenericName=Text Editor
            GenericName[de]=Texteditor
            Comment=Edits\stext
            Comment[de]=Bearbeitet Text
            Icon=full-icon
            Exec=full --edit %F
            TryExec=full
            Path=/tmp
            Terminal=true
            NoDisplay=true
            DBusActivatable=true
            StartupNotify=1
            StartupWMClass=FullClass
            Categories=Utility;TextEditor;
            Keywords=text;edit\;ing;
            Keywords[de]=Text;Bearbeiten
            MimeType=text/plain;text/x-csrc
            OnlyShowIn=GNOME;KDE;
            NotShowIn=XFCE;
            Implements=org.freedesktop.Full;
            Actions=new-window;private;
            [Desktop Action new-window]
            Name=New Window
            Name[de]=Neues Fenster
            Icon=window-new
            Exec=full --new-window
            [Desktop Action private]
            Name=Private
            Exec=full --private %U
            [Desktop Action unlisted]
            Name=Never
            """, DesktopLocale.Parse("de_DE.UTF-8"));

        Assert.Equal(DesktopEntryType.Application, entry.Type);
        Assert.Equal("Voll", entry.Name);
        Assert.Equal("Texteditor", entry.GenericName);
        Assert.Equal("Bearbeitet Text", entry.Comment);
        Assert.Equal("full-icon", entry.Icon);
        Assert.Equal("full --edit %F", entry.Exec);
        Assert.Equal(["full", "--edit"], entry.Argv);
        Assert.Equal("full --edit", entry.CommandLine);
        Assert.Equal("full", entry.TryExec);
        Assert.Equal("/tmp", entry.WorkingDirectory);
        Assert.True(entry.Terminal);
        Assert.True(entry.NoDisplay);
        Assert.True(entry.DBusActivatable);
        Assert.True(entry.StartupNotify);
        Assert.Equal("FullClass", entry.StartupWMClass);
        Assert.Null(entry.Url);
        Assert.Equal(["Utility", "TextEditor"], entry.Categories);
        Assert.Equal(["Text", "Bearbeiten"], entry.Keywords);
        Assert.Equal(["text/plain", "text/x-csrc"], entry.MimeType);
        Assert.Equal(["GNOME", "KDE"], entry.OnlyShowIn);
        Assert.Equal(["XFCE"], entry.NotShowIn);
        Assert.Equal(["org.freedesktop.Full"], entry.Implements);
        Assert.Equal(2, entry.Actions.Count);
        Assert.Equal(new DesktopAction("new-window", "Neues Fenster", "window-new", "full --new-window"), entry.Actions[0]);
        Assert.Equal(new DesktopAction("private", "Private", null, "full --private %U"), entry.Actions[1]);
        Assert.Equal(["full", "--private"], entry.Actions[1].Argv);
    }

    [Fact]
    public void The_unlocalised_keys_and_the_escaped_semicolon_read_without_a_locale()
    {
        var entry = Parse("plain.desktop", "[Desktop Entry]\nType=Application\nName=Plain\nComment=Edits\\stext\nKeywords=text;edit\\;ing;\nExec=plain\n", DesktopLocale.None);
        Assert.Equal("Plain", entry.Name);
        Assert.Equal("Edits text", entry.Comment);
        Assert.Equal(["text", "edit;ing"], entry.Keywords);
        Assert.Null(entry.GenericName);
        Assert.Empty(entry.Actions);
        Assert.False(entry.Terminal);
    }

    [Fact]
    public void A_link_a_directory_and_a_typeless_file_carry_their_type()
    {
        Assert.Equal(DesktopEntryType.Link, Parse("link.desktop", "[Desktop Entry]\nType=Link\nName=A Link\nURL=https://example.org\n", DesktopLocale.None).Type);
        Assert.Equal("https://example.org", Parse("link.desktop", "[Desktop Entry]\nType=Link\nName=A Link\nURL=https://example.org\n", DesktopLocale.None).Url);
        Assert.Equal(DesktopEntryType.Directory, Parse("dir.desktop", "[Desktop Entry]\nType=Directory\nName=A Dir\n", DesktopLocale.None).Type);
        Assert.Equal(DesktopEntryType.Unknown, Parse("none.desktop", "[Desktop Entry]\nName=No Type\n", DesktopLocale.None).Type);
        Assert.Equal(DesktopEntryType.Unknown, Parse("odd.desktop", "[Desktop Entry]\nType=Service\nName=Odd\n", DesktopLocale.None).Type);
    }

    [Fact]
    public void A_second_entry_group_and_keys_outside_any_group_are_ignored()
    {
        var entry = Parse("twice.desktop", "Name=Outside\n[Desktop Entry]\nType=Application\nName=First\n[Desktop Entry]\nName=Second\nNoDisplay=true\n[Other]\nName=Other\n", DesktopLocale.None);
        Assert.Equal("First", entry.Name);
        Assert.False(entry.NoDisplay);
    }

    [Fact]
    public void Listable_reads_the_flags_the_spec_names_and_not_terminal()
    {
        var current = new HashSet<string>(StringComparer.Ordinal) { "GNOME" };
        Assert.True(Parse("ok.desktop", "[Desktop Entry]\nType=Application\nName=Ok\nExec=ok\n", DesktopLocale.None).IsListable());
        Assert.True(Parse("term.desktop", "[Desktop Entry]\nType=Application\nName=Term\nExec=term\nTerminal=true\n", DesktopLocale.None).IsListable());
        Assert.False(Parse("nodisplay.desktop", "[Desktop Entry]\nType=Application\nName=Hidden\nExec=x\nNoDisplay=true\n", DesktopLocale.None).IsListable());
        Assert.False(Parse("link.desktop", "[Desktop Entry]\nType=Link\nName=Link\nURL=x\n", DesktopLocale.None).IsListable());
        Assert.False(Parse("noexec.desktop", "[Desktop Entry]\nType=Application\nName=No Exec\n", DesktopLocale.None).IsListable());
        Assert.False(Parse("tryexec.desktop", "[Desktop Entry]\nType=Application\nName=Gone\nExec=x\nTryExec=/nonexistent/binary\n", DesktopLocale.None).IsListable());
        Assert.False(Parse("tryexec-path.desktop", "[Desktop Entry]\nType=Application\nName=Gone\nExec=x\nTryExec=no-such-binary-anywhere\n", DesktopLocale.None).IsListable());
        Assert.True(Parse("tryexec-sh.desktop", "[Desktop Entry]\nType=Application\nName=Here\nExec=x\nTryExec=sh\n", DesktopLocale.None).IsListable());
        Assert.True(Parse("tryexec-abs.desktop", "[Desktop Entry]\nType=Application\nName=Here\nExec=x\nTryExec=/bin/sh\n", DesktopLocale.None).IsListable());
        var only = Parse("only.desktop", "[Desktop Entry]\nType=Application\nName=Only\nExec=x\nOnlyShowIn=GNOME;\n", DesktopLocale.None);
        Assert.False(only.IsListable());
        Assert.True(only.IsListable(current));
        Assert.False(only.IsListable(new HashSet<string> { "KDE" }));
        var not = Parse("not.desktop", "[Desktop Entry]\nType=Application\nName=Not\nExec=x\nNotShowIn=GNOME;\n", DesktopLocale.None);
        Assert.True(not.IsListable());
        Assert.False(not.IsListable(current));
    }

    [Fact]
    public void Launch_argv_wraps_a_terminal_entry_and_declines_without_a_terminal()
    {
        var plain = Parse("plain.desktop", "[Desktop Entry]\nType=Application\nName=Plain\nExec=\"/opt/My App/bin\" %U\n", DesktopLocale.None);
        Assert.Equal(["/opt/My App/bin"], plain.LaunchArgv(null)!);
        Assert.Equal(["/opt/My App/bin"], plain.LaunchArgv(["foot"])!);
        Assert.NotSame(plain.LaunchArgv(null), plain.LaunchArgv(null));
        var term = Parse("term.desktop", "[Desktop Entry]\nType=Application\nName=Term\nExec=htop --tree\nTerminal=true\n", DesktopLocale.None);
        Assert.Null(term.LaunchArgv(null));
        Assert.Null(term.LaunchArgv([]));
        Assert.Equal(["foot", "-a", "x", "-e", "htop", "--tree"], term.LaunchArgv(["foot", "-a", "x", "-e"])!);
        Assert.Equal(["xdg-terminal-exec", "htop", "--tree"], term.LaunchArgv(["xdg-terminal-exec"])!);
        var empty = Parse("empty.desktop", "[Desktop Entry]\nType=Application\nName=Empty\nExec=%U\n", DesktopLocale.None);
        Assert.Null(empty.LaunchArgv(["foot"]));
    }

    [Fact]
    public void Text_parses_by_the_file_rules_and_records_the_path_it_was_given()
    {
        var entry = DesktopEntryReader.ParseText(
            "[Desktop Entry]\r\nType=Application\r\nName=Remote\r\nName[de]=Fern\r\nExec=remote %U\r\nCategories=Network;\r\n",
            "/remote/share/applications/remote.desktop",
            "remote.desktop",
            DesktopLocale.Parse("de"));

        Assert.NotNull(entry);
        Assert.Equal("Fern", entry.Name);
        Assert.Equal("/remote/share/applications/remote.desktop", entry.Path);
        Assert.Equal("remote.desktop", entry.Id);
        Assert.Equal(["remote"], entry.Argv);
        Assert.Equal(["Network"], entry.Categories);
        Assert.Null(DesktopEntryReader.ParseText("[Desktop Entry]\nType=Application\nName=Gone\nHidden=true\n", "/x/gone.desktop", "gone.desktop", DesktopLocale.None));
        Assert.Null(DesktopEntryReader.ParseText("[Desktop Entry]\nType=Application\nExec=x\n", "/x/nameless.desktop", "nameless.desktop", DesktopLocale.None));
    }

    [Fact]
    public void A_consumer_can_answer_try_exec_for_another_machine()
    {
        var entry = DesktopEntryReader.ParseText(
            "[Desktop Entry]\nType=Application\nName=Elsewhere\nExec=elsewhere\nTryExec=/opt/elsewhere/bin\n",
            "/remote/elsewhere.desktop",
            "elsewhere.desktop",
            DesktopLocale.None)!;

        Assert.False(entry.IsListable());
        Assert.False(entry.IsListable(null, null));
        Assert.True(entry.IsListable(null, static _ => true));
        Assert.False(entry.IsListable(null, static _ => false));
        string? asked = null;
        Assert.True(entry.IsListable(null, name => (asked = name) is not null));
        Assert.Equal("/opt/elsewhere/bin", asked);
        var plain = DesktopEntryReader.ParseText("[Desktop Entry]\nType=Application\nName=Plain\nExec=plain\n", "/remote/plain.desktop", "plain.desktop", DesktopLocale.None)!;
        Assert.True(plain.IsListable(null, static _ => false));
    }

    private DesktopEntry Parse(string file, string content, DesktopLocale locale)
    {
        var path = Path.Combine(_root, file);
        Directory.CreateDirectory(_root);
        File.WriteAllText(path, content);
        var entry = DesktopEntryReader.Parse(path, file, locale);
        Assert.NotNull(entry);
        Assert.Equal(file, entry.Id);
        Assert.Equal(path, entry.Path);
        return entry;
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
}
