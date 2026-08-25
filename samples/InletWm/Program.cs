using System.CommandLine;
using Basin.Cli;
using Basin.WindowManager;

using Basin.Diagnostics;

namespace InletWm;

internal static class Program
{
    private static int Main(string[] args)
    {
        var cli = new BasinCommand(
            "Implementation of tinyrvm in .NET\n"
            + "\n"
            + "  Super+Return    spawn a terminal\n"
            + "  Super+J/K       cycle focus\n"
            + "  Super+Q         close the focused window\n"
            + "  Super+H/L       shrink/grow the master area\n"
            + "  Super+Shift+E   exit the Wayland session");
        var socketOption = cli.Add(CommonOptions.Socket());
        var terminalOption = cli.Add(new Option<string>("--terminal")
        {
            Description = "terminal Super+Return spawns, otherwise INLETWM_TERMINAL",
            HelpName = "CMD",
            DefaultValueFactory = _ => Environment.GetEnvironmentVariable("INLETWM_TERMINAL") ?? "foot",
        });
        var traceOption = cli.Add(CommonOptions.Trace());
        var exitAfterOption = cli.Add(CommonOptions.ExitAfter());

        return cli.Run(args, result =>
        {
            var trace = result.GetValue(traceOption);
            cli.ConfigureLogging(result, trace);
            return Run(
                BasinLog.For("InletWm"),
                result.GetValue(socketOption),
                result.GetValue(terminalOption)!,
                trace,
                result.GetValue(exitAfterOption));
        });
    }

    private static int Run(BasinLogger log, string? socket, string terminal, bool trace, int exitAfter)
    {
        RiverWindowManager wm;
        try
        {
            wm = new RiverWindowManager(socket);
        }
        catch (InvalidOperationException error)
        {
            log.Error($"{error.Message}");
            return 1;
        }

        using (wm)
        {
            var tiler = new Tiler(wm, terminal, trace, log);
            var refused = false;
            wm.Unavailable += () =>
            {
                refused = true;
                log.Error($"the compositor refused window management, another window manager is already running.");
            };

            var reached = false;
            if (exitAfter > 0)
            {
                var seen = 0;
                wm.Manage += context =>
                {
                    seen += context.NewWindows.Count;
                    if (seen >= exitAfter && !reached)
                    {
                        reached = true;
                        log.Info($"laid out {seen} window(s), stopping");
                        wm.Stop();
                    }
                };
            }

            log.Info($"managing with protocol version {wm.Version}; bindings v{wm.Bindings.Version}, layer shell {(wm.LayerShell is null ? "absent" : "present")}");

            if (trace)
            {
                IWmEventSource? report = null;
                report = wm.Loop.AddTimer(() =>
                {
                    ReportLatency(log, wm);
                    report!.UpdateTimer(5000);
                });
                report.UpdateTimer(5000);
            }

            wm.Run();

            if (trace)
            {
                ReportLatency(log, wm);
            }

            if (exitAfter > 0 && !reached)
            {
                log.Error($"the session ended before {exitAfter} window(s) were laid out.");
                return 1;
            }

            return refused ? 1 : 0;
        }
    }

    private static void ReportLatency(BasinLogger log, RiverWindowManager wm)
    {
        log.Debug($"manage {wm.ManageLatency}");
        log.Debug($"render {wm.RenderLatency}");
    }
}
