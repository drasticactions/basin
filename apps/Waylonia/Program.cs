using System.CommandLine;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Threading;
using Basin;
using Basin.Avalonia;
using Basin.Cli;
using Basin.Diagnostics;
using Basin.Scene;

namespace Waylonia;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        HostSession.Capture();
        var cli = new BasinCommand("Runs a Wayland application inside an Avalonia window.");
        var frames = cli.Add(CommonOptions.Frames());
        var screenshot = cli.Add(CommonOptions.Screenshot());
        frames.Hidden = true;
        screenshot.Hidden = true;
        var socketOption = cli.Add(new Option<string?>("--socket")
        {
            Description = "the Wayland socket name to bind, where the platform has one.",
        });
        var listenOption = cli.Add(CommonOptions.WaypipeListen());
        var sshOption = cli.Add(new Option<string?>("--ssh")
        {
            Description = "run the command on this host under waypipe server, and accept the channel. " +
                "A [hosts.NAME] profile in the config file matches first; otherwise an ssh destination.",
        });
        var configOption = cli.Add(new Option<string?>("--config")
        {
            Description = "read this file instead of ~/.config/waylonia/waylonia.toml; false skips the file.",
        });
        var gpuOption = cli.Add(CommonOptions.Gpu());
        var audioOption = cli.Add(new Option<bool>("--audio")
        {
            Description = "play the remote session's sound on this host. Off by default.",
        });
        var audioFormatOption = cli.Add(new Option<string>("--audio-format")
        {
            Description = "the format the remote session's sound is sent in: f32 or s16.",
            HelpName = "NAME",
            DefaultValueFactory = _ => "f32",
            Hidden = true,
        });
        audioFormatOption.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<string>() is not ("f32" or "s16"))
            {
                result.AddError("--audio-format takes f32 or s16");
            }
        });
        var desktopOption = cli.Add(new Option<string?>("--desktop")
        {
            Description = "run a whole Linux desktop in one window. A [desktops.NAME] profile in the config " +
                $"file matches first; otherwise a built-in recipe: {DesktopRecipes.Names}.",
            HelpName = "NAME",
        });
        var desktopSizeOption = cli.Add(new Option<string?>("--desktop-size")
        {
            Description = "the screen window's initial size, WxH. The default is 80% of its screen.",
            HelpName = "WxH",
        });
        var videoOption = cli.Add(CommonOptions.Video());
        var compressOption = cli.Add(CommonOptions.Compress());
        var command = new Argument<string[]>("command")
        {
            Description = "run this program as a local Wayland client.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        cli.Command.Arguments.Add(command);

        return cli.Run(args, result =>
        {
            cli.ConfigureLogging(result);
            var log = BasinLog.For("waylonia");
            var configValue = result.GetValue(configOption);
            var config = Config.Load(
                configValue == "false",
                configValue == "false" ? null : configValue,
                log);
            var commandText = result.GetValue(command) is { Length: > 0 } parts ? string.Join(' ', parts) : null;
            var listen = result.GetValue(listenOption);
            var ssh = result.GetValue(sshOption);
            HostProfile? profile = null;
            if (ssh is not null && config.Hosts.TryGetValue(ssh, out var named))
            {
                profile = named;
                ssh = named.Ssh;
            }

            var desktopName = result.GetValue(desktopOption);
            DesktopRecipe? recipe = null;
            IReadOnlyList<string> desktopEnv = [];
            (int Width, int Height)? desktopSize = null;
            DesktopProfile? desktopProfile = null;
            if (desktopName is not null)
            {
                if (commandText is not null)
                {
                    log.Error($"--desktop is the command; a trailing command runs beside it");
                    return 1;
                }

                config.Desktops.TryGetValue(desktopName, out desktopProfile);
                var recipeName = desktopProfile?.Recipe ?? desktopName;
                var found = DesktopRecipes.Find(recipeName);
                if (found is null && recipeName != "custom")
                {
                    log.Error($"--desktop {desktopName} names no recipe; the built-in ones are {DesktopRecipes.Names}");
                    return 1;
                }

                var desktopCommand = desktopProfile?.Command ?? found?.Command;
                if (desktopCommand is null)
                {
                    log.Error($"[desktops.{desktopName}] has recipe = \"custom\" and no command");
                    return 1;
                }

                recipe = (found ?? new DesktopRecipe(
                    desktopName, desktopCommand, desktopName.ToUpperInvariant(),
                    [], Bus: true, Gpu: true, Video: null, SoftwareFallback: false))
                    with { Command = desktopCommand };
                desktopEnv = desktopProfile?.Env ?? [];
                if (ParseSize(result.GetValue(desktopSizeOption) ?? desktopProfile?.Size) is { } size)
                {
                    desktopSize = size;
                }
                else if ((result.GetValue(desktopSizeOption) ?? desktopProfile?.Size) is { } bad)
                {
                    log.Error($"--desktop-size takes WxH, not '{bad}'");
                    return 1;
                }

                if (ssh is null && desktopProfile?.Host is { } desktopHost)
                {
                    ssh = config.Hosts.TryGetValue(desktopHost, out var hostProfile)
                        ? hostProfile.Ssh
                        : desktopHost;
                    profile ??= config.Hosts.GetValueOrDefault(desktopHost);
                }

                if (ssh is null && !OperatingSystem.IsLinux())
                {
                    log.Error($"--desktop with no --ssh runs the session on this machine, which needs Linux");
                    return 1;
                }
            }

            if (listen is not null && ssh is not null)
            {
                log.Error($"--waypipe-listen accepts a channel and --ssh opens its own");
                return 1;
            }

            var sshCommand = commandText ?? profile?.Command;
            if (recipe is not null)
            {
                sshCommand = null;
            }

            if (recipe is not null && listen is not null)
            {
                log.Error($"--desktop starts the session and --waypipe-listen waits for one someone else started");
                return 1;
            }

            if (listen is not null && commandText is not null)
            {
                log.Error($"a trailing command spawns a local client and --waypipe-listen waits for a remote one");
                return 1;
            }

            if (ssh is null && listen is null && recipe is null)
            {
                commandText ??= config.Command;
            }

            if (recipe is null && MissingClientSource(
                OperatingSystem.IsLinux(), OperatingSystem.IsWindows(), ssh, listen, commandText))
            {
                return cli.Usage(result);
            }

            var compress = result.GetResult(compressOption) is null or { Implicit: true }
                ? profile?.Compress ?? config.Compress ?? "lz4"
                : result.GetValue(compressOption)!;

            var gpu = result.GetResult(gpuOption) is null or { Implicit: true }
                ? desktopProfile?.Gpu ?? recipe?.Gpu ?? config.Gpu ?? false
                : result.GetValue(gpuOption);

            var audio = Waylonia.Audio.WayloniaAudio.Wanted(
                result.GetResult(audioOption) is null or { Implicit: true }
                    ? config.Audio ?? false
                    : result.GetValue(audioOption),
                ssh,
                listen);

            var video = result.GetResult(videoOption) is null or { Implicit: true }
                ? desktopProfile?.Video ?? recipe?.Video ?? config.Video ?? "none"
                : result.GetValue(videoOption)!;
            var videoCodec = video.Split(',')[0];
            var videoHardware = CommonOptions.VideoDecodesOnGpu(video);
            var videoRemote = CommonOptions.VideoRemoteSetting(video);
            Basin.Capabilities.IVideoDecoder? decoder = null;
            if (videoCodec != "none")
            {
                gpu = true;
                decoder = Basin.Video.FFmpeg.FFmpegVideoDecoder.TryCreate(videoHardware, out var whyNot);
                if (decoder is null)
                {
                    log.Error($"--video {video} needs a decoder and none is available: {whyNot}");
                    return 1;
                }

                var wanted = videoCodec switch
                {
                    "vp9" => Basin.Capabilities.VideoCodec.Vp9,
                    "av1" => Basin.Capabilities.VideoCodec.Av1,
                    _ => Basin.Capabilities.VideoCodec.H264,
                };
                if (!decoder.Supports(wanted))
                {
                    log.Error($"the system FFmpeg decodes no {videoCodec}");
                    return 1;
                }
            }

            var status = WayloniaApp.Run(new WayloniaRun(
                result.GetValue(frames),
                result.GetValue(screenshot),
                result.GetValue(socketOption) ?? config.Socket,
                ssh is null ? commandText : null,
                listen,
                ssh,
                ssh is null ? null : sshCommand,
                compress switch
                {
                    "none" => Basin.Transport.Waypipe.WaypipeCompression.None,
                    "zstd" => Basin.Transport.Waypipe.WaypipeCompression.Zstd,
                    _ => Basin.Transport.Waypipe.WaypipeCompression.Lz4,
                },
                gpu,
                audio,
                result.GetValue(audioFormatOption)!,
                videoCodec == "none"
                    ? null
                    : videoRemote is null ? videoCodec : $"{videoCodec},{videoRemote}",
                decoder,
                config.XWayland,
                config.Tray,
                config.Clipboard,
                config.Drag,
                config.FollowCursor,
                config.GtkDpi,
                config.Hotkeys,
                recipe,
                desktopEnv,
                desktopSize,
                config.CaptureChord));
            cli.ReportFrames(WayloniaApp.Rendered);
            return status;
        });
    }

    internal static (int Width, int Height)? ParseSize(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var parts = text.Split('x', 'X');
        return parts.Length == 2
            && int.TryParse(parts[0], out var width)
            && int.TryParse(parts[1], out var height)
            && width > 0
            && height > 0
            ? (width, height)
            : null;
    }

    internal static bool MissingClientSource(
        bool linux, bool windows, string? ssh, string? listen, string? command)
    {
        if (linux || ssh is not null || listen is not null)
        {
            return false;
        }

        return windows || command is null;
    }
}
