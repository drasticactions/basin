using Waylonia;
using Xunit;

using Basin.Diagnostics;

namespace Waylonia.Tests;

public sealed class ConfigTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "waylonia-tests-" + Guid.NewGuid().ToString("n"));

    private string Write(string toml)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "waylonia.toml");
        File.WriteAllText(path, toml);
        return path;
    }

    private static Config Load(string path) => Config.Load(false, path, BasinLogger.None);

    [Fact]
    public void Every_top_level_key_reaches_the_config()
    {
        var config = Load(Write("""
            compress = "none"
            socket = "wayland-9"
            command = "foot"
            """));

        Assert.Equal("none", config.Compress);
        Assert.Equal("wayland-9", config.Socket);
        Assert.Equal("foot", config.Command);
    }

    [Fact]
    public void Audio_is_off_unless_the_file_turns_it_on()
    {
        Assert.Null(Load(Write("compress = \"none\"")).Audio);
        Assert.True(Load(Write("audio = true")).Audio);
        Assert.False(Load(Write("audio = false")).Audio);
    }

    [Fact]
    public void A_command_array_joins_into_one_line()
    {
        var config = Load(Write("""
            command = ["tmux", "new -A", "-s main"]
            """));

        Assert.Equal("tmux new -A -s main", config.Command);
    }

    [Fact]
    public void An_unknown_compression_keeps_the_default()
    {
        var config = Load(Write("""
            compress = "brotli"
            """));

        Assert.Null(config.Compress);
    }

    [Theory]
    [InlineData("lz4")]
    [InlineData("zstd")]
    [InlineData("none")]
    public void Every_compression_the_channel_carries_is_read(string name)
    {
        var config = Load(Write($"""
            compress = "{name}"
            """));

        Assert.Equal(name, config.Compress);
    }

    [Fact]
    public void The_host_toggles_default_on_and_turn_off_individually()
    {
        var defaults = Load(Write("socket = \"wayland-1\""));
        Assert.True(defaults.XWayland);
        Assert.True(defaults.Tray);
        Assert.True(defaults.Clipboard);
        Assert.True(defaults.Drag);
        Assert.True(defaults.FollowCursor);

        var config = Load(Write("""
            [host]
            xwayland = false
            drag = false
            follow-cursor = false
            """));

        Assert.False(config.XWayland);
        Assert.True(config.Tray);
        Assert.True(config.Clipboard);
        Assert.False(config.Drag);
        Assert.False(config.FollowCursor);
    }

    [Fact]
    public void A_host_profile_carries_its_ssh_command_and_compression()
    {
        var config = Load(Write("""
            [hosts.dev]
            ssh = "user@devbox"
            command = "tmux new -A -s main"
            compress = "none"
            """));

        var profile = Assert.Contains("dev", config.Hosts);
        Assert.Equal("user@devbox", profile.Ssh);
        Assert.Equal("tmux new -A -s main", profile.Command);
        Assert.Equal("none", profile.Compress);
    }

    [Fact]
    public void A_host_profile_without_an_ssh_destination_is_skipped()
    {
        var config = Load(Write("""
            [hosts.broken]
            command = "foot"

            [hosts.dev]
            ssh = "user@devbox"
            """));

        Assert.DoesNotContain("broken", config.Hosts);
        Assert.Contains("dev", config.Hosts);
    }

    [Fact]
    public void An_unknown_section_leaves_the_rest_of_the_file_standing()
    {
        var config = Load(Write("""
            socket = "wayland-9"

            [devbox]
            ssh = "user@devbox"
            """));

        Assert.Equal("wayland-9", config.Socket);
        Assert.Empty(config.Hosts);
    }

    [Fact]
    public void A_file_that_does_not_parse_keeps_every_default()
    {
        var config = Load(Write("compress = \"none"));

        Assert.Null(config.Compress);
        Assert.Null(config.Socket);
        Assert.True(config.XWayland);
    }

    [Fact]
    public void An_explicit_path_that_is_missing_writes_nothing()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "absent.toml");
        var config = Config.Load(false, path, BasinLogger.None);

        Assert.False(File.Exists(path));
        Assert.Null(config.Socket);
    }

    [Fact]
    public void Skipping_the_file_ignores_one_that_is_there()
    {
        var path = Write("""
            socket = "wayland-9"
            compress = "none"
            """);

        var config = Config.Load(true, path, BasinLogger.None);

        Assert.Null(config.Socket);
        Assert.Null(config.Compress);
    }

    [Fact]
    public void The_default_path_gets_a_placeholder_that_parses_back_to_the_defaults()
    {
        var previous = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Directory.CreateDirectory(_directory);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _directory);
        try
        {
            var config = Config.Load(false, null, BasinLogger.None);
            var path = Path.Combine(_directory, "waylonia", "waylonia.toml");

            Assert.True(File.Exists(path));
            Assert.Null(config.Socket);

            var reloaded = Config.Load(false, null, BasinLogger.None);
            Assert.Null(reloaded.Socket);
            Assert.Null(reloaded.Compress);
            Assert.Null(reloaded.Command);
            Assert.True(reloaded.XWayland);
            Assert.Empty(reloaded.Hosts);
            Assert.Empty(reloaded.Hotkeys);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previous);
        }
    }

    [Fact]
    public void A_hotkey_table_becomes_parsed_chords()
    {
        var config = Load(Write("""
            [hotkeys]
            "ctrl+alt+t" = "foot"
            "super+shift+return" = ["foot", "-e", "htop"]
            """));

        Assert.Equal(2, config.Hotkeys.Count);
        var terminal = Assert.Single(config.Hotkeys, hotkey => hotkey.Chord == "ctrl+alt+t");
        Assert.Equal(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, terminal.Modifiers);
        Assert.Equal("t", terminal.Key);
        Assert.Equal("foot", terminal.Command);

        var other = Assert.Single(config.Hotkeys, hotkey => hotkey.Chord == "super+shift+return");
        Assert.Equal(HotkeyModifiers.Super | HotkeyModifiers.Shift, other.Modifiers);
        Assert.Equal("foot -e htop", other.Command);
    }

    [Fact]
    public void A_hotkey_without_a_command_is_dropped()
    {
        var config = Load(Write("""
            [hotkeys]
            "ctrl+alt+t" = ""
            "ctrl+alt+u" = "foot"
            """));

        var kept = Assert.Single(config.Hotkeys);
        Assert.Equal("ctrl+alt+u", kept.Chord);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
