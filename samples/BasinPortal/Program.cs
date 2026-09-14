using System.CommandLine;
using Basin.Cli;
using Basin.Diagnostics;
using Basin.Portal;

namespace BasinPortal;

internal static class Program
{
    private static int Main(string[] args)
    {
        var cli = new BasinCommand("sample xdg-desktop-portal backend for basin");
        var socketOption = cli.Add(CommonOptions.Socket());
        var busNameOption = cli.Add(new Option<string>("--bus-name")
        {
            Description = "the D-Bus name to own",
            HelpName = "NAME",
            DefaultValueFactory = _ => PortalBus.DefaultBusName,
        });
        var installOption = cli.Add(new Option<bool>("--install-files")
        {
            Description = "write the xdg-desktop-portal registration files and the D-Bus service file under $XDG_DATA_HOME, print their paths and exit",
        }, report: false);
        var promptsOption = cli.Add(new Option<string>("--prompts")
        {
            Description = "the toolkit that draws the prompts: skia or avalonia",
            HelpName = "NAME",
            DefaultValueFactory = _ => "skia",
        }.AcceptOnlyFromAmong("skia", "avalonia"));
        var traceOption = cli.Add(CommonOptions.Trace());

        return cli.Run(args, result =>
        {
            var trace = result.GetValue(traceOption);
            cli.ConfigureLogging(result, trace);
            if (result.GetValue(installOption))
            {
                var (portalPath, configPath, servicePath) = PortalRegistration.Write(PortalRegistration.AllInterfaces, Environment.ProcessPath);
                BasinReport.Line($"PORTAL {portalPath}");
                BasinReport.Line($"PORTAL {configPath}");
                if (servicePath is not null)
                {
                    BasinReport.Line($"PORTAL {servicePath}");
                    PortalRegistration.ReloadBus();
                }

                return 0;
            }

            var log = BasinLog.For("xdg-desktop-portal-basin");
            var socket = result.GetValue(socketOption);
            if (socket is { Length: > 0 })
            {
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", socket);
            }

            if (!PortalBus.HasSessionBus)
            {
                log.Error($"this session has no D-Bus session bus (DBUS_SESSION_BUS_ADDRESS is unset); a portal backend has nothing to answer");
                return 1;
            }

            PortalApp app;
            try
            {
                var prompts = result.GetValue(promptsOption) == "avalonia" ? PromptStyle.Avalonia : PromptStyle.Skia;
                app = new PortalApp(socket, result.GetValue(busNameOption)!, prompts, log);
            }
            catch (Exception error) when (error is InvalidOperationException or Wayland.WaylandException)
            {
                log.Error($"{error.Message}");
                return 1;
            }

            using (app)
            {
                return app.Run();
            }
        });
    }
}
