using Basin.Cli;
using Tomlyn;
using Tomlyn.Model;

using Basin.Diagnostics;

namespace Waylonia;

internal sealed class Config
{
    public string? Compress { get; private set; }

    public bool? Gpu { get; private set; }

    public string? Video { get; private set; }

    public string? Socket { get; private set; }

    public string? Command { get; private set; }

    public bool XWayland { get; private set; } = true;

    public bool Tray { get; private set; } = true;

    public bool Clipboard { get; private set; } = true;

    public bool Drag { get; private set; } = true;

    public bool FollowCursor { get; private set; } = true;

    public IReadOnlyDictionary<string, HostProfile> Hosts { get; private set; } =
        new Dictionary<string, HostProfile>();

    public IReadOnlyList<Hotkey> Hotkeys { get; private set; } = [];

    public static Config Load(bool skipFile, string? path, BasinLogger log)
    {
        var config = new Config();
        if (skipFile)
        {
            return config;
        }

        var explicitPath = path is not null;
        path ??= TomlConfig.DefaultPath("waylonia");
        if (!explicitPath && !File.Exists(path))
        {
            WritePlaceholder(path, log);
            return config;
        }

        if (TomlConfig.Read(path, log) is not { } table)
        {
            return config;
        }

        foreach (var (name, value) in table)
        {
            if (value is TomlTable && name is not ("host" or "hosts" or "hotkeys"))
            {
                log.Warn($"{path} has an unknown section '[{name}]', ignoring it; a remote host profile is [hosts.{name}]");
            }
        }

        config.Compress = Compression(table, "compress", log);
        if (table.TryGetValue("gpu", out var gpu) && gpu is bool gpuEnabled)
        {
            config.Gpu = gpuEnabled;
        }

        if (table.TryGetValue("video", out var video) && video is string videoCodec)
        {
            if (Basin.Cli.CommonOptions.IsVideoChoice(videoCodec))
            {
                config.Video = videoCodec;
            }
            else
            {
                log.Warn($"Invalid --video, ignoring '{videoCodec}'");
            }
        }

        if (table.TryGetValue("socket", out var socket) && socket is string socketName && socketName.Length > 0)
        {
            config.Socket = socketName;
        }

        config.Command = CommandText(table, "command");

        if (table.TryGetValue("host", out var host) && host is TomlTable hostTable)
        {
            config.XWayland = Toggle(hostTable, "xwayland", config.XWayland);
            config.Tray = Toggle(hostTable, "tray", config.Tray);
            config.Clipboard = Toggle(hostTable, "clipboard", config.Clipboard);
            config.Drag = Toggle(hostTable, "drag", config.Drag);
            config.FollowCursor = Toggle(hostTable, "follow-cursor", config.FollowCursor);
        }

        if (table.TryGetValue("hosts", out var hosts) && hosts is TomlTable hostsTable)
        {
            var parsed = new Dictionary<string, HostProfile>();
            foreach (var (name, value) in hostsTable)
            {
                if (value is not TomlTable profileTable)
                {
                    continue;
                }

                if (!profileTable.TryGetValue("ssh", out var destination)
                    || destination is not string sshDestination
                    || sshDestination.Length == 0)
                {
                    log.Warn($"host profile '{name}' has no ssh destination, skipping");
                    continue;
                }

                parsed[name] = new HostProfile(
                    sshDestination,
                    CommandText(profileTable, "command"),
                    Compression(profileTable, "compress", log));
            }

            config.Hosts = parsed;
        }

        if (table.TryGetValue("hotkeys", out var hotkeys) && hotkeys is TomlTable hotkeyTable)
        {
            var parsed = new List<Hotkey>();
            foreach (var (chord, value) in hotkeyTable)
            {
                if (Hotkey.Parse(chord, CommandText(value), log) is { } hotkey)
                {
                    parsed.Add(hotkey);
                }
            }

            config.Hotkeys = parsed;
        }

        return config;
    }

    private static void WritePlaceholder(string path, BasinLogger log)
    {
        const string placeholder = """
            # The waypipe channel compression: "lz4", "zstd" or "none".
            #compress = "lz4"

            # Advertise dmabuf to the remote session. Each remote buffer is
            # backed by a host memory region. Off keeps the session shm-only.
            #gpu = true

            # Ask the remote waypipe to encode buffer updates as video, and
            # decode them here with the system FFmpeg. Implies gpu. Append
            # ",hw" to decode on this host's GPU when it has a device.
            #video = "h264"

            # The Wayland socket name to bind, where the platform has one.
            #socket = "wayland-9"

            # The local client a bare `waylonia` spawns; a string or an argv array.
            # Ignored when --ssh or --waypipe-listen selects a remote session.
            #command = "foot"

            # Host desktop integration; every toggle defaults to on.
            #[host]
            #xwayland = true
            #tray = true
            #clipboard = true
            #drag = true
            # Open each new client window on the screen the pointer is on, rather
            # than wherever the host desktop would put it.
            #follow-cursor = true

            # Remote-session profiles:
            #[hosts.dev]
            #ssh = "user@devbox"
            #command = "tmux new -A -s main"
            #compress = "none"

            # Host-global hotkeys.
            #[hotkeys]
            #"ctrl+alt+t" = "foot"

            """;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(stream);
            writer.Write(placeholder);
            log.Info($"wrote a placeholder config to {path}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log.Warn($"cannot write a placeholder config to {path}: {error.Message}");
        }
    }

    private static bool Toggle(TomlTable table, string key, bool fallback) =>
        TomlConfig.Flag(table, key, fallback);

    private static string? Compression(TomlTable table, string key, BasinLogger log)
    {
        if (!table.TryGetValue(key, out var value) || value is not string name)
        {
            return null;
        }

        if (name is "lz4" or "zstd" or "none")
        {
            return name;
        }

        log.Warn($"compress takes lz4, zstd or none, ignoring '{name}'");
        return null;
    }

    private static string? CommandText(TomlTable table, string key) =>
        table.TryGetValue(key, out var value) ? CommandText(value) : null;

    private static string? CommandText(object? value)
    {
        var text = value switch
        {
            string command => command.Trim(),
            TomlArray array => string.Join(
                ' ',
                array.OfType<string>().Select(static part => part.Trim()).Where(static part => part.Length > 0)),
            _ => string.Empty,
        };
        return text.Length > 0 ? text : null;
    }
}
