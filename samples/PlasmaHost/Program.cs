using System.CommandLine;
using Basin.Cli;

using Basin.Diagnostics;

namespace PlasmaHost;

internal static class Program
{
    private static int Main(string[] args)
    {
        var cli = new BasinCommand("A compositor whose shell is plasmashell: KDE draws every pixel of chrome.");
        var renderer = cli.Add(CommonOptions.Renderer(Basin.Renderers.RendererCatalog.Names, "vulkan"));
        var backend = cli.Add(CommonOptions.Backend(
            [BackendKind.Nested, BackendKind.Drm, BackendKind.Headless]));
        var outputs = cli.Add(CommonOptions.Outputs());
        var scales = cli.Add(CommonOptions.Scales());
        var shell = cli.Add(new Option<string>("--shell")
        {
            Description = "the shell to start as a child, or false to start none",
            HelpName = "PATH|false",
            DefaultValueFactory = _ => "plasmashell",
        });
        var config = cli.Add(new Option<string?>("--config")
        {
            Description = "read this kwinoutputconfig.json, or false to read none",
            HelpName = "PATH",
        });
        var screenshot = cli.Add(CommonOptions.Screenshot());
        var frames = cli.Add(CommonOptions.Frames());

        return cli.Run(args, result =>
        {
            cli.ConfigureLogging(result);
            var shellValue = result.GetValue(shell);
            var options = new PlasmaHostOptions
            {
                Backend = result.GetValue(backend).Kind,
                Renderer = result.GetValue(renderer)!,
                Outputs = result.GetValue(outputs),
                Scales = result.GetValue(scales)!,
                Frames = result.GetValue(frames),
                Screenshot = result.GetValue(screenshot),
                Config = result.GetValue(config),
                Shell = string.Equals(shellValue, "false", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : shellValue,
            };
            var status = PlasmaHost.Run(options, BasinLog.For("PlasmaHost"), out var rendered);
            cli.ReportFrames(rendered);
            return status;
        });
    }
}
