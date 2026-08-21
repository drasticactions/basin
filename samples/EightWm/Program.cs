using System.CommandLine;
using Basin.Cli;
using Microsoft.Extensions.Logging;

namespace EightWm;

internal static class Program
{
    private static int Main(string[] args)
    {
        var cli = new BasinCommand("A Windows 8 tablet shell on basin.");
        var renderer = cli.Add(CommonOptions.Renderer(Basin.Renderers.RendererCatalog.Names, "vulkan"));
        var backend = cli.Add(CommonOptions.Backend(
            [BackendKind.Nested, BackendKind.Drm, BackendKind.Headless], acceptsSocketFd: true));
        var outputs = cli.Add(new Option<int>("--outputs")
        {
            Description = "how many outputs to create",
            HelpName = "N",
            DefaultValueFactory = _ => 1,
        });
        var scales = cli.Add(CommonOptions.Scales());
        var config = cli.Add(new Option<string?>("--config")
        {
            Description = "read this config file, or 'false' to read none",
            HelpName = "PATH",
        });
        var hotCorners = cli.Add(new Option<string>("--hot-corners")
        {
            Description = "corner triggers for a mouse: on | off",
            HelpName = "STATE",
            DefaultValueFactory = _ => "on",
        }.AcceptOnlyFromAmong("on", "off"));
        var animations = cli.Add(new Option<string>("--animations")
        {
            Description = "the whole animation catalog: on | off",
            HelpName = "STATE",
            DefaultValueFactory = _ => "on",
        }.AcceptOnlyFromAmong("on", "off"));
        var startOutput = cli.Add(new Option<int>("--start-output")
        {
            Description = "which output shows Start at startup",
            HelpName = "N",
            DefaultValueFactory = _ => 0,
        });
        var minWidth = cli.Add(new Option<int>("--min-width")
        {
            Description = "narrowest a cell may become, in logical pixels",
            HelpName = "N",
            DefaultValueFactory = _ => 500,
        });
        var edgeBand = cli.Add(new Option<int>("--edge-band")
        {
            Description = "how wide the edge bands are, in logical pixels",
            HelpName = "N",
            DefaultValueFactory = _ => 20,
        });
        var xwayland = cli.Add(new Option<string>("--xwayland")
        {
            Description = "host X11 clients through Xwayland: on | off",
            HelpName = "STATE",
            DefaultValueFactory = _ => "on",
        }.AcceptOnlyFromAmong("on", "off"));
        var screenshot = cli.Add(CommonOptions.Screenshot());
        var client = cli.Add(CommonOptions.Client());
        var frames = cli.Add(CommonOptions.Frames());

        return cli.Run(args, result =>
        {
            var chosen = result.GetValue(backend);
            using var loggers = cli.CreateLoggerFactory(result);
            var explicitly = new HashSet<string>(StringComparer.Ordinal);
            void Note<T>(string name, Option<T> option)
            {
                if (result.GetResult(option) is { Implicit: false })
                {
                    explicitly.Add(name);
                }
            }

            Note("hot_corners", hotCorners);
            Note("animations", animations);
            Note("edge_band", edgeBand);
            Note("min_width", minWidth);
            Note("start_output", startOutput);

            var options = new ShellOptions
            {
                Backend = chosen.Kind,
                Renderer = result.GetValue(renderer)!,
                Outputs = result.GetValue(outputs),
                Scales = result.GetValue(scales)!,
                Frames = result.GetValue(frames),
                Screenshot = result.GetValue(screenshot),
                Client = result.GetValue(client),
                ConfigPath = result.GetValue(config),
                HotCorners = result.GetValue(hotCorners) == "on",
                Animations = result.GetValue(animations) == "on",
                StartOutput = result.GetValue(startOutput),
                MinWidth = result.GetValue(minWidth),
                EdgeBand = result.GetValue(edgeBand),
                XWayland = result.GetValue(xwayland) == "on",
                SocketFd = chosen.SocketFd,
                Explicit = explicitly,
            };

            var status = Shell.Run(options, loggers.CreateLogger("EightWm"), out var rendered);
            cli.ReportFrames(rendered);
            return status;
        });
    }
}
