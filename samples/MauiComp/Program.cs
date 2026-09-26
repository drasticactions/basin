using System.CommandLine;
using Basin.Cli;
using Basin.Diagnostics;

namespace MauiComp;

internal static class Program
{
    private static int Main(string[] args)
    {
        var cli = new BasinCommand("a compositor whose chrome is .NET MAUI, drawn through Avalonia inside the compositor.");
        var renderer = cli.Add(CommonOptions.Renderer(Basin.Renderers.RendererCatalog.Names, "vulkan"));
        var backend = cli.Add(CommonOptions.Backend(
            [BackendKind.Nested, BackendKind.Drm, BackendKind.Headless], acceptsSocketFd: true));
        var outputs = cli.Add(CommonOptions.Outputs());
        var scales = cli.Add(CommonOptions.Scales());
        var theme = cli.Add(new Option<string>("--theme")
        {
            Description = "shell chrome variant: light | dark",
            HelpName = "VARIANT",
            DefaultValueFactory = _ => "light",
        }.AcceptOnlyFromAmong("light", "dark"));
        var background = cli.Add(new Option<string?>("--background")
        {
            Description = "an image to draw behind the windows, on every output",
            HelpName = "PATH",
        });
        var configPath = cli.Add(CommonOptions.Config("maui-comp"));
        var screenshot = cli.Add(CommonOptions.Screenshot());
        var frames = cli.Add(CommonOptions.Frames());
        _ = IpcCli.AddOption(cli);

        var settings = new MauiCompConfig();
        string? fatal = null;
        cli.Prepare(result =>
        {
            cli.ConfigureLogging(result);
            settings = MauiCompConfig.Load(result.GetValue(configPath), BasinLog.For("MauiComp"), out fatal);
            settings.Theme = BasinCommand.Effective(result, theme, settings.Theme, settings.FromFile.Contains("theme"))!;
            settings.Background = BasinCommand.Effective(result, background, settings.Background, settings.FromFile.Contains("background"));
            if (result.GetResult(theme) is not (null or { Implicit: true }))
            {
                settings.FromFlags.Add("theme");
            }

            if (result.GetResult(background) is not (null or { Implicit: true }))
            {
                settings.FromFlags.Add("background");
            }
        });

        return cli.Run(args, result =>
        {
            var chosen = result.GetValue(backend);
            if (fatal is { } failure)
            {
                BasinLog.For("MauiComp").Error($"{failure}");
                return 1;
            }

            var options = new MauiCompOptions
            {
                Ipc = IpcCli.Read(cli, result),
                Backend = chosen.Kind,
                Renderer = result.GetValue(renderer)!,
                Outputs = result.GetValue(outputs),
                Scales = result.GetValue(scales)!,
                Frames = result.GetValue(frames),
                Screenshot = result.GetValue(screenshot),
                Background = settings.Background,
                Theme = settings.Theme == "dark"
                    ? Basin.UI.Avalonia.UIThemeVariant.Dark
                    : Basin.UI.Avalonia.UIThemeVariant.Light,
                ConfigPath = result.GetValue(configPath),
                Config = settings,
                SocketFd = chosen.SocketFd,
            };

            var status = MauiComp.Run(options, BasinLog.For("MauiComp"), out var rendered);
            cli.ReportFrames(rendered);
            return status;
        });
    }
}
