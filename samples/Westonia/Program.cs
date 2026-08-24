using System.CommandLine;
using Basin.Cli;
using Microsoft.Extensions.Logging;

namespace Westonia;

internal static class Program
{
    private static int Main(string[] args)
    {
        var cli = new BasinCommand("weston's desktop shell, drawn by Avalonia inside the compositor.");
        var renderer = cli.Add(CommonOptions.Renderer(Basin.Renderers.RendererCatalog.Names, "vulkan"));
        var backend = cli.Add(CommonOptions.Backend(
            [BackendKind.Nested, BackendKind.Drm, BackendKind.Headless], acceptsSocketFd: true));
        var outputs = cli.Add(CommonOptions.Outputs());
        var scales = cli.Add(CommonOptions.Scales());
        var config = cli.Add(new Option<string?>("--config")
        {
            Description = "read this weston.ini, or false to read none",
            HelpName = "PATH",
        });
        var theme = cli.Add(new Option<string>("--theme")
        {
            Description = "shell chrome variant: light | dark",
            HelpName = "VARIANT",
            DefaultValueFactory = _ => "light",
        }.AcceptOnlyFromAmong("light", "dark"));
        var xwayland = cli.Add(new Option<bool>("--xwayland")
        {
            Description = "start Xwayland even when weston.ini does not ask for it",
        });
        var screenshot = cli.Add(CommonOptions.Screenshot());
        var frames = cli.Add(CommonOptions.Frames());

        return cli.Run(args, result =>
        {
            var chosen = result.GetValue(backend);
            using var loggers = cli.CreateLoggerFactory(result);
            var configValue = result.GetValue(config);
            var options = new WestoniaOptions
            {
                Backend = chosen.Kind,
                Renderer = result.GetValue(renderer)!,
                Outputs = result.GetValue(outputs),
                Scales = result.GetValue(scales)!,
                Frames = result.GetValue(frames),
                Screenshot = result.GetValue(screenshot),
                XWayland = result.GetValue(xwayland),
                Theme = result.GetValue(theme) == "dark"
                    ? Basin.UI.Avalonia.UIThemeVariant.Dark
                    : Basin.UI.Avalonia.UIThemeVariant.Light,
                SocketFd = chosen.SocketFd,
                ConfigPath = string.Equals(configValue, "false", StringComparison.OrdinalIgnoreCase) ? null : configValue,
                NoConfig = string.Equals(configValue, "false", StringComparison.OrdinalIgnoreCase),
            };

            var status = Westonia.Run(options, loggers.CreateLogger("Westonia"), out var rendered);
            cli.ReportFrames(rendered);
            return status;
        });
    }
}
