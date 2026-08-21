using Basin.Seat;
using Xunit;

namespace Basin.Tests;

public sealed class SystemKeymapTests : IDisposable
{
    private static readonly string[] EnvironmentNames =
    [
        "XKB_DEFAULT_RULES", "XKB_DEFAULT_MODEL", "XKB_DEFAULT_LAYOUT",
        "XKB_DEFAULT_VARIANT", "XKB_DEFAULT_OPTIONS",
    ];

    private readonly string _root = Directory.CreateTempSubdirectory("basin-keymap").FullName;
    private readonly Dictionary<string, string?> _environment = [];

    public SystemKeymapTests()
    {
        foreach (var name in EnvironmentNames)
        {
            _environment[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    public void Dispose()
    {
        foreach (var (name, value) in _environment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        Directory.Delete(_root, recursive: true);
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void A_system_with_no_configuration_names_nothing()
    {
        var names = SystemKeymap.Read(_root);
        Assert.Null(names.Layout);
        Assert.Null(names.Model);
    }

    [Fact]
    public void The_xorg_file_localectl_writes_is_read()
    {
        Write("etc/X11/xorg.conf.d/00-keyboard.conf", """
            Section "InputClass"
                    Identifier "system-keyboard"
                    MatchIsKeyboard "on"
                    Option "XkbLayout" "jp"
                    Option "XkbModel" "jp106"
                    Option "XkbOptions" "terminate:ctrl_alt_bksp"
            EndSection
            """);

        var names = SystemKeymap.Read(_root);
        Assert.Equal("jp", names.Layout);
        Assert.Equal("jp106", names.Model);
        Assert.Equal("terminate:ctrl_alt_bksp", names.Options);
        Assert.Null(names.Variant);
    }

    [Fact]
    public void Debians_keyboard_file_is_read()
    {
        Write("etc/default/keyboard", """
            XKBMODEL="pc105"
            XKBLAYOUT="fr"
            XKBVARIANT="oss"
            XKBOPTIONS=""
            """);

        var names = SystemKeymap.Read(_root);
        Assert.Equal("fr", names.Layout);
        Assert.Equal("oss", names.Variant);

        Assert.Null(names.Options);
    }

    [Fact]
    public void The_console_file_carries_the_xkb_names_on_a_recent_systemd()
    {
        Write("etc/vconsole.conf", """
            KEYMAP=jp106
            XKBLAYOUT=jp
            XKBMODEL=jp106
            """);

        var names = SystemKeymap.Read(_root);
        Assert.Equal("jp", names.Layout);
        Assert.Equal("jp106", names.Model);
    }

    [Fact]
    public void A_console_keymap_alone_goes_through_systemds_table()
    {
        Write("etc/vconsole.conf", "KEYMAP=jp106\n");
        Write("usr/share/systemd/kbd-model-map", """
            # consolelayout xlayout xmodel xvariant xoptions
            us              us      pc105   -        -
            jp106           jp      jp106   -        terminate:ctrl_alt_bksp
            """);

        var names = SystemKeymap.Read(_root);
        Assert.Equal("jp", names.Layout);
        Assert.Equal("jp106", names.Model);
        Assert.Equal("terminate:ctrl_alt_bksp", names.Options);

        Assert.Null(names.Variant);
    }

    [Fact]
    public void The_xorg_file_wins_over_the_console_one()
    {
        Write("etc/X11/xorg.conf.d/00-keyboard.conf", """
            Section "InputClass"
                    Option "XkbLayout" "de"
            EndSection
            """);
        Write("etc/vconsole.conf", "XKBLAYOUT=jp\n");

        Assert.Equal("de", SystemKeymap.Read(_root).Layout);
    }

    [Fact]
    public void The_environment_answers_for_itself()
    {
        Write("etc/vconsole.conf", "XKBLAYOUT=jp\n");
        Environment.SetEnvironmentVariable("XKB_DEFAULT_LAYOUT", "se");
        try
        {
            Assert.Null(SystemKeymap.Read(_root).Layout);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XKB_DEFAULT_LAYOUT", null);
        }
    }
}
