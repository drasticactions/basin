using System.CommandLine;
using Basin.Cli;
using Basin.WindowManager;
using Microsoft.Extensions.Logging;

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
            using var loggers = cli.CreateLoggerFactory(result, trace);
            return Run(
                loggers.CreateLogger("InletWm"),
                result.GetValue(socketOption),
                result.GetValue(terminalOption)!,
                trace,
                result.GetValue(exitAfterOption));
        });
    }

    private static int Run(ILogger log, string? socket, string terminal, bool trace, int exitAfter)
    {
        RiverWindowManager wm;
        try
        {
            wm = new RiverWindowManager(socket);
        }
        catch (InvalidOperationException error)
        {
            log.LogError("{Reason}", error.Message);
            return 1;
        }

        using (wm)
        {
            var tiler = new Tiler(wm, terminal, trace, log);
            var refused = false;
            wm.Unavailable += () =>
            {
                refused = true;
                log.LogError("the compositor refused window management, another window manager is already running.");
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
                        log.LogInformation("laid out {Count} window(s), stopping", seen);
                        wm.Stop();
                    }
                };
            }

            log.LogInformation(
                "managing with protocol version {Version}; bindings v{BindingsVersion}, layer shell {LayerShell}",
                wm.Version, wm.Bindings.Version, wm.LayerShell is null ? "absent" : "present");

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
                log.LogError("the session ended before {Count} window(s) were laid out.", exitAfter);
                return 1;
            }

            return refused ? 1 : 0;
        }
    }

    private static void ReportLatency(ILogger log, RiverWindowManager wm)
    {
        log.LogDebug("manage {Latency}", wm.ManageLatency);
        log.LogDebug("render {Latency}", wm.RenderLatency);
    }
}
