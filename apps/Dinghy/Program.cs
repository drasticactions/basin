using System.CommandLine;
using Basin.Cli;
using Basin.WindowManager;
using Microsoft.Extensions.Logging;

namespace Dinghy;

internal static class Program
{
    private static int Main(string[] args)
    {
        var cli = new BasinCommand("A stacking window manager, running as a client of river or Inlet.");
        var socketOption = cli.Add(CommonOptions.Socket());
        var configOption = cli.Add(new Option<bool>("--config")
        {
            Description = "read ~/.config/dinghy/dinghy.toml, otherwise use the built-in defaults",
            DefaultValueFactory = _ => true,
        });
        var traceOption = cli.Add(CommonOptions.Trace());
        var exitAfterOption = cli.Add(CommonOptions.ExitAfter());

        return cli.Run(args, result =>
        {
            var trace = result.GetValue(traceOption);
            using var loggers = cli.CreateLoggerFactory(result, trace);
            return Run(
                loggers.CreateLogger("Dinghy"),
                result.GetValue(socketOption),
                !result.GetValue(configOption),
                trace,
                result.GetValue(exitAfterOption));
        });
    }

    private static int Run(ILogger log, string? socket, bool noConfig, bool trace, int exitAfter)
    {
        if (socket is { Length: > 0 })
        {
            Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", socket);
        }

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
            var manager = new Manager(wm, trace, noConfig, log);
            var refused = false;
            wm.Unavailable += () =>
            {
                refused = true;
                log.LogError("the compositor refused window management — another window manager is already running.");
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
                        log.LogInformation("managed {Count} window(s), stopping", seen);
                        wm.Stop();
                    }
                };
            }

            log.LogInformation(
                "managing with protocol version {Version}; bindings v{BindingsVersion}, layer shell {LayerShell}",
                wm.Version, wm.Bindings.Version, wm.LayerShell is null ? "absent" : "present");

            wm.Run();

            if (exitAfter > 0 && !reached)
            {
                log.LogError("the session ended before {Count} window(s) were managed.", exitAfter);
                return 1;
            }

            return refused ? 1 : 0;
        }
    }
}
