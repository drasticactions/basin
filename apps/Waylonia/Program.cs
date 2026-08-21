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
            using var loggers = cli.CreateLoggerFactory(result);
            var configValue = result.GetValue(configOption);
            var config = Config.Load(
                configValue == "false",
                configValue == "false" ? null : configValue,
                loggers.CreateLogger("waylonia"));
            var commandText = result.GetValue(command) is { Length: > 0 } parts ? string.Join(' ', parts) : null;
            var listen = result.GetValue(listenOption);
            var ssh = result.GetValue(sshOption);
            HostProfile? profile = null;
            if (ssh is not null && config.Hosts.TryGetValue(ssh, out var named))
            {
                profile = named;
                ssh = named.Ssh;
            }

            if (listen is not null && ssh is not null)
            {
                Console.Error.WriteLine("--waypipe-listen accepts a channel and --ssh opens its own");
                return 1;
            }

            var sshCommand = commandText ?? profile?.Command;
            if (listen is not null && commandText is not null)
            {
                Console.Error.WriteLine("a trailing command spawns a local client and --waypipe-listen waits for a remote one");
                return 1;
            }

            if (ssh is null && listen is null)
            {
                commandText ??= config.Command;
            }

            if (MissingClientSource(
                OperatingSystem.IsLinux(), OperatingSystem.IsWindows(), ssh, listen, commandText))
            {
                return cli.Usage(result);
            }

            var compress = result.GetResult(compressOption) is null or { Implicit: true }
                ? profile?.Compress ?? config.Compress ?? "lz4"
                : result.GetValue(compressOption)!;

            var gpu = result.GetResult(gpuOption) is null or { Implicit: true }
                ? config.Gpu ?? false
                : result.GetValue(gpuOption);

            var video = result.GetResult(videoOption) is null or { Implicit: true }
                ? config.Video ?? "none"
                : result.GetValue(videoOption)!;
            var videoCodec = video.Split(',')[0];
            var videoHardware = video.EndsWith(",hw", StringComparison.Ordinal);
            Basin.Capabilities.IVideoDecoder? decoder = null;
            if (videoCodec != "none")
            {
                gpu = true;
                decoder = Basin.Video.FFmpeg.FFmpegVideoDecoder.TryCreate(videoHardware, out var whyNot);
                if (decoder is null)
                {
                    Console.Error.WriteLine($"--video {video} needs a decoder and none is available: {whyNot}");
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
                    Console.Error.WriteLine($"the system FFmpeg decodes no {videoCodec}");
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
                videoCodec == "none" ? null : videoCodec,
                decoder,
                config.XWayland,
                config.Tray,
                config.Clipboard,
                config.Drag,
                config.FollowCursor,
                config.Hotkeys));
            cli.ReportFrames(WayloniaApp.Rendered);
            return status;
        });
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
