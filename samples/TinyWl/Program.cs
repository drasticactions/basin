using System.CommandLine;
using Basin;
using Basin.Backend.Drm;
using Basin.Backend.Libinput;
using Basin.Backend.Wayland;
using Basin.Cli;
using Basin.Diagnostics;
using Basin.Scene;
using Basin.Seat;
using Basin.Shell.Xdg;
using Wayland;
using Wayland.Server;
using Xkb;

namespace TinyWl;

internal static class Program
{
    private static int Main(string[] args)
    {
        var cli = new BasinCommand(
            ".NET implementation of tinywl.\n"
            + "\n"
            + "  Alt+Escape   terminate the compositor\n"
            + "  Alt+F1       cycle between windows");
        var rendererOption = cli.Add(CommonOptions.Renderer(
            Basin.Renderers.RendererCatalog.Names, "auto"));
        var backendOption = cli.Add(CommonOptions.Backend([BackendKind.Nested, BackendKind.Drm]));
        var startupOption = cli.Add(new Option<string?>("--startup", "-s")
        {
            Description = "run this command once the compositor is up",
            HelpName = "CMD",
        });

        return cli.Run(args, result =>
        {
            cli.ConfigureLogging(result);
            return Run(
                BasinLog.For("TinyWl"),
                result.GetValue(backendOption).Kind == BackendKind.Drm,
                result.GetValue(rendererOption) is { } name && name != "auto" ? name : null,
                result.GetValue(startupOption));
        });
    }

    private static int Run(BasinLogger log, bool drm, string? renderer, string? startupCommand)
    {
        BasinCounters.Reset();
        int status;
        try
        {
            using var compositor = new TinyWl(drm, renderer, log);
            status = compositor.Run(startupCommand);
        }
        catch (Exception error) when (error is InvalidOperationException or DllNotFoundException or IOException)
        {
            log.Error($"{error.Message}");
            return 1;
        }

        if (BasinCounters.LiveObjects != 0)
        {
            log.Warn($"{BasinCounters.LiveObjects} objects still live at exit");
        }

        return status;
    }
}
