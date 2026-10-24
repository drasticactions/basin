using System.CommandLine;
using Basin.Cli;
using Basin.Diagnostics;

namespace TabWm;

internal static class Program
{
    private static int Main(string[] args)
    {
        var cli = new BasinCommand("A tabbed compositor: each browser-style tab holds a workspace.");
        var renderer = cli.Add(CommonOptions.Renderer(Basin.Renderers.RendererCatalog.Names, "gl"));
        var backend = cli.Add(CommonOptions.Backend(
            [BackendKind.Nested, BackendKind.Drm, BackendKind.Headless], acceptsSocketFd: true));
        var outputs = cli.Add(CommonOptions.Outputs());
        var scales = cli.Add(CommonOptions.Scales());
        var config = cli.Add(CommonOptions.Config("tab-wm"));
        var engine = cli.Add(new Option<string?>("--engine")
        {
            Description = "AvaUnion engine: auto | native | managed (overrides [theme] engine)",
            HelpName = "ENGINE",
        }.AcceptOnlyFromAmong("auto", "native", "managed"));
        var noRestore = cli.Add(new Option<bool>("--no-restore")
        {
            Description = "do not reopen the tabs of the last session",
        });
        var noXWayland = cli.Add(new Option<bool>("--no-xwayland")
        {
            Description = "do not start Xwayland",
        });
        var command = cli.Add(CommonOptions.Command("run `sh -c <command>` in the first tab of the primary output at startup"));
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
            var options = new TabWmOptions
            {
                Ipc = IpcCli.Read(cli, result),
                Backend = chosen.Kind,
                Handoff = CommonOptions.ReadHandoff(result, handoff),
                Renderer = result.GetValue(renderer)!,
                Outputs = result.GetValue(outputs),
                Scales = result.GetValue(scales)!,
                Frames = result.GetValue(frames),
                Screenshot = result.GetValue(screenshot),
                SocketFd = chosen.SocketFd,
                ConfigPath = noConfig ? null : configValue,
                NoConfig = noConfig,
                Engine = result.GetValue(engine),
                NoRestore = result.GetValue(noRestore),
                NoXWayland = result.GetValue(noXWayland),
                Command = result.GetValue(command),
            };

            var status = TabWm.Run(options, BasinLog.For("TabWm"), out var rendered);
            cli.ReportFrames(rendered);
            return status;
        });
    }
}
