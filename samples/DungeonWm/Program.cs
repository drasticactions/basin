using System.CommandLine;
using Basin.Cli;
using Basin.Diagnostics;

namespace DungeonWm;

internal static class Program
{
    private static int Main(string[] args)
    {
        var cli = new BasinCommand("A compositor that hangs windows on the walls of a 3D dungeon you walk through.");
        var renderer = cli.Add(CommonOptions.Renderer(Basin.Renderers.RendererCatalog.Names, "vulkan"));
        var backend = cli.Add(CommonOptions.Backend(
            [BackendKind.Nested, BackendKind.Drm, BackendKind.Headless], acceptsSocketFd: true));
        var outputs = cli.Add(CommonOptions.Outputs());
        var width = cli.Add(CommonOptions.Width(1280));
        var height = cli.Add(CommonOptions.Height(720));
        var scales = cli.Add(CommonOptions.Scales());
        var config = cli.Add(CommonOptions.Config("dungeon-wm"));
        var xwayland = cli.Add(new Option<bool>("--xwayland")
        {
            Description = "start Xwayland so X11 clients can connect",
        });
        var command = cli.Add(CommonOptions.Command());
        var screenshot = cli.Add(CommonOptions.Screenshot());
        var frames = cli.Add(CommonOptions.Frames());
        var handoff = cli.Add(CommonOptions.Handoff());
        _ = IpcCli.AddOption(cli);

        return cli.Run(args, result =>
        {
            var chosen = result.GetValue(backend);
            cli.ConfigureLogging(result);
            var log = BasinLog.For("DungeonWm");
            var configValue = result.GetValue(config);
            var noConfig = string.Equals(configValue, "false", StringComparison.OrdinalIgnoreCase);
            var settings = DungeonWmConfig.Load(noConfig ? null : configValue, noConfig, log, out var configPath, out var fatal);
            if (fatal is not null)
            {
                log.Error($"config: {fatal}");
                return 1;
            }

            var rendererName = Environment.GetEnvironmentVariable("BASIN_RENDERER") is { Length: > 0 }
                ? result.GetValue(renderer)!
                : BasinCommand.Effective(result, renderer, settings.Renderer)!;
            var options = new DungeonWmOptions
            {
                Ipc = IpcCli.Read(cli, result),
                Backend = chosen.Kind,
                Handoff = CommonOptions.ReadHandoff(result, handoff),
                Renderer = rendererName,
                Settings = settings,
                Outputs = Math.Max(1, result.GetValue(outputs)),
                Width = result.GetValue(width),
                Height = result.GetValue(height),
                Scales = result.GetValue(scales)!,
                Frames = result.GetValue(frames),
                Screenshot = result.GetValue(screenshot),
                XWayland = result.GetValue(xwayland),
                Command = result.GetValue(command),
                SocketFd = chosen.SocketFd,
                ConfigPath = configPath,
                NoConfig = noConfig,
            };

            var status = DungeonWm.Run(options, log, out var rendered);
            cli.ReportFrames(rendered);
            return status;
        });
    }
}
