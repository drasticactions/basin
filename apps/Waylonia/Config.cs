using Basin.Cli;
using Tomlyn;
using Tomlyn.Model;

using Basin.Diagnostics;

namespace Waylonia;

internal sealed class Config
{
    public string? Compress { get; private set; }

    public bool? Gpu { get; private set; }

    public bool? Audio { get; private set; }

    public string? Video { get; private set; }

    public string? Socket { get; private set; }

    public string? Command { get; private set; }

    public bool XWayland { get; private set; } = true;

    public bool Tray { get; private set; } = true;

    public bool Clipboard { get; private set; } = true;

    public bool Drag { get; private set; } = true;

    public bool FollowCursor { get; private set; } = true;

    public bool GtkDpi { get; private set; } = true;

    public string CaptureChord { get; private set; } = "double:RightControl";

    public IReadOnlyDictionary<string, DesktopProfile> Desktops { get; private set; } =
        new Dictionary<string, DesktopProfile>();

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
            if (value is TomlTable && name is not ("host" or "hosts" or "hotkeys" or "desktops"))
            {
                log.Warn($"{path} has an unknown section '[{name}]', ignoring it; a remote host profile is [hosts.{name}]");
            }
        }

        config.Compress = Compression(table, "compress", log);
        if (table.TryGetValue("gpu", out var gpu) && gpu is bool gpuEnabled)
        {
            config.Gpu = gpuEnabled;
        }

        if (table.TryGetValue("audio", out var audio) && audio is bool audioEnabled)
        {
            config.Audio = audioEnabled;
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
            config.GtkDpi = Toggle(hostTable, "gtk-dpi", config.GtkDpi);
            if (hostTable.TryGetValue("capture-chord", out var chord)
                && chord is string chordText
                && chordText.Trim().Length > 0)
            {
                config.CaptureChord = chordText.Trim();
            }
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

        if (table.TryGetValue("desktops", out var desktops) && desktops is TomlTable desktopsTable)
        {
            var parsed = new Dictionary<string, DesktopProfile>();
            foreach (var (name, value) in desktopsTable)
            {
                if (value is not TomlTable profileTable)
                {
                    continue;
                }

                parsed[name] = new DesktopProfile(
                    name,
                    Text(profileTable, "recipe"),
                    Text(profileTable, "host"),
                    Text(profileTable, "size"),
                    CommandText(profileTable, "command"),
                    Assignments(profileTable, "env"),
                    profileTable.TryGetValue("gpu", out var desktopGpu) && desktopGpu is bool flag ? flag : null,
                    Text(profileTable, "video"));
            }

            config.Desktops = parsed;
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
            # ",hw" to decode on this host's GPU when it has a device, and
            # ",hwenc", ",swenc", ",hwdec", ",swdec" or ",bpf=B" to say where
            # the remote encodes and decodes, and at how many bits per frame.
            #video = "h264,hw,hwenc,bpf=7.5e5"

            # Play the remote session's sound on this host. It is captured
            # from a sink of its own on the remote and streamed over the same
            # ssh connection, which costs about 384 kB/s. Off by default,
            # because a local client already plays to this host's sound server.
            #audio = true

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
            # Read an --ssh session's GTK settings through a staged copy whose
            # gtk-xft-dpi is 96, so a remote desktop's own display scaling does
            # not size GTK windows twice. Off leaves the remote config alone.
            #gtk-dpi = true

            # Remote-session profiles:
            #[hosts.dev]
            #ssh = "user@devbox"
            #command = "tmux new -A -s main"
            #compress = "none"

            # Take the host's own keyboard and pointer for a nested desktop.
            # A double tap of one modifier within 400 ms toggles it.
            #capture-chord = "double:RightControl"

            # Whole-desktop sessions. --desktop NAME matches one of these
            # first, then a built-in recipe name: sway, niri, plasma, cosmic
            # or xfce.
            #[desktops.plasma]
            #recipe = "plasma"
            #host = "lab"
            #size = "1920x1080"
            #command = "startplasma-wayland"
            #env = ["QT_QPA_PLATFORM=wayland"]
            #gpu = false
            #video = "none"

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

    private static string? Text(TomlTable table, string key) =>
        table.TryGetValue(key, out var value) && value is string text && text.Trim().Length > 0
            ? text.Trim()
            : null;

    private static IReadOnlyList<string> Assignments(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value))
        {
            return [];
        }

        return value switch
        {
            string single when single.Trim().Length > 0 => [single.Trim()],
            TomlArray array => array.OfType<string>()
                .Select(static part => part.Trim())
                .Where(static part => part.Length > 0)
                .ToArray(),
            _ => [],
        };
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
