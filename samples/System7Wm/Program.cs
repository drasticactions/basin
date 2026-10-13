using System.CommandLine;
using Basin.Cli;
using Basin.Diagnostics;

namespace System7Wm;

internal static class Program
{
    private static int Main(string[] args)
    {
        var cli = new BasinCommand("A Mac OS System 7 desktop on basin, its chrome drawn by Avalonia inside the compositor.");
        var renderer = cli.Add(CommonOptions.Renderer(Basin.Renderers.RendererCatalog.Names, "vulkan"));
        var backend = cli.Add(CommonOptions.Backend(
            [BackendKind.Nested, BackendKind.Drm, BackendKind.Headless], acceptsSocketFd: true));
        var outputs = cli.Add(CommonOptions.Outputs());
        var scales = cli.Add(CommonOptions.Scales());
        var config = cli.Add(new Option<string?>("--config")
        {
            Description = "read this config file, or false to read none",
            HelpName = "PATH",
        });
        var xwayland = cli.Add(new Option<bool?>("--xwayland")
        {
            Description = "host X11 clients through Xwayland: true or false, beating [windows] xwayland (default true)",
            HelpName = "BOOL",
            Arity = ArgumentArity.ZeroOrOne,
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
            var configValue = result.GetValue(config);
            var noConfig = string.Equals(configValue, "false", StringComparison.OrdinalIgnoreCase);
            var options = new System7Options
            {
                Ipc = IpcCli.Read(cli, result),
                Backend = chosen.Kind,
                Handoff = CommonOptions.ReadHandoff(result, handoff),
                Renderer = result.GetValue(renderer)!,
                Outputs = result.GetValue(outputs),
                Scales = result.GetValue(scales)!,
                Frames = result.GetValue(frames),
                Screenshot = result.GetValue(screenshot),
                XWayland = result.GetValue(xwayland),
                Command = result.GetValue(command),
                SocketFd = chosen.SocketFd,
                ConfigPath = noConfig ? null : configValue,
                NoConfig = noConfig,
            };

            var status = System7Shell.Run(options, BasinLog.For("System7Wm"), out var rendered);
            cli.ReportFrames(rendered);
            return status;
        });
    }
}
