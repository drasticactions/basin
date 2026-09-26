using System.CommandLine;
using Basin.Cli;
using ModelContextProtocol.Server;

namespace BasinMcp;

internal static class Program
{
    private static int Main(string[] args)
    {
        var cli = new BasinCommand(
            "Serve a running basin compositor to an MCP host over stdio. Every method of the compositor's control\n"
            + "socket becomes a tool. The filters are a convenience, not a boundary: the socket's uid check is.\n"
            + "\n"
            + "  claude mcp add basin basin-mcp\n"
            + "  claude mcp add basin-ro -- basin-mcp --read-only")
        {
            ReportsOptions = false,
        };
        var socket = cli.Add(new Option<string?>("--socket")
        {
            Description = "the control socket, otherwise $BASIN_SOCKET or the one basin-*.sock in $XDG_RUNTIME_DIR",
            HelpName = "PATH",
        });
        var readOnly = cli.Add(new Option<bool>("--read-only")
        {
            Description = "publish only the methods that do not change the session",
        });
        var allow = cli.Add(new Option<string?>("--allow")
        {
            Description = "publish only the methods that match one of these comma-separated globs, for example 'windows/*,capture/*'",
            HelpName = "GLOBS",
        });
        var deny = cli.Add(new Option<string?>("--deny")
        {
            Description = "never publish the methods that match one of these comma-separated globs, for example 'session/quit,outputs/*'",
            HelpName = "GLOBS",
        });
        var launch = cli.Add(new Option<bool>("--launch")
        {
            Description = "start the command after -- with its control socket in a private directory, and stop it when stdin closes",
        });
        var command = new Argument<string[]>("command")
        {
            Description = "with --launch, the compositor and its arguments",
            Arity = ArgumentArity.ZeroOrMore,
        };
        cli.Command.Arguments.Add(command);
        var maxDimension = cli.Add(new Option<int>("--max-dimension")
        {
            Description = "the longest side of an inline screenshot; 0 sends full size",
            HelpName = "N",
            DefaultValueFactory = _ => McpBridgeOptions.DefaultMaxDimension,
        });

        return cli.Run(args, result =>
        {
            cli.ConfigureLogging(result);
            McpMethodFilter filter;
            try
            {
                filter = new McpMethodFilter(
                    result.GetValue(readOnly), McpGlob.ParseList(result.GetValue(allow)), McpGlob.ParseList(result.GetValue(deny)));
            }
            catch (FormatException exception)
            {
                Console.Error.WriteLine($"basin-mcp: {exception.Message}");
                return 1;
            }

            if (result.GetValue(maxDimension) < 0)
            {
                Console.Error.WriteLine("basin-mcp: --max-dimension is 0 or more");
                return 1;
            }

            var words = result.GetValue(command) ?? [];
            if (!result.GetValue(launch))
            {
                if (words.Length > 0)
                {
                    Console.Error.WriteLine("basin-mcp: a command is only taken with --launch");
                    return 1;
                }

                return Serve(new McpBridgeOptions
                {
                    SocketPath = result.GetValue(socket),
                    Filter = filter,
                    MaxDimension = result.GetValue(maxDimension),
                });
            }

            if (result.GetValue(socket) is not null)
            {
                Console.Error.WriteLine("basin-mcp: --launch makes its own socket, so --socket does not go with it");
                return 1;
            }

            return Launch(words, filter, result.GetValue(maxDimension)).GetAwaiter().GetResult();
        });
    }

    private static int Serve(McpBridgeOptions options)
    {
        var bridge = new McpBridge(options);
        try
        {
            return bridge.RunAsync(new StdioServerTransport("basin-mcp"), CancellationToken.None).GetAwaiter().GetResult();
        }
        finally
        {
            bridge.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static async Task<int> Launch(string[] command, McpMethodFilter filter, int maxDimension)
    {
        using var stop = new CancellationTokenSource();
        using var terminate = Stop(stop, System.Runtime.InteropServices.PosixSignal.SIGTERM);
        using var interrupt = Stop(stop, System.Runtime.InteropServices.PosixSignal.SIGINT);
        using var hangup = Stop(stop, System.Runtime.InteropServices.PosixSignal.SIGHUP);
        if (McpLauncher.Start(command, out var error) is not { } launcher)
        {
            Console.Error.WriteLine($"basin-mcp: {error}");
            return 1;
        }

        McpBridge? bridge = null;
        try
        {
            if (!await launcher.WaitForSocketAsync(TimeSpan.FromSeconds(30), stop.Token).ConfigureAwait(false))
            {
                if (launcher.HasExited)
                {
                    Console.Error.WriteLine($"basin-mcp: {command[0]} exited with status {launcher.ExitCode} before it made its socket");
                    return launcher.ExitCode == 0 ? 1 : launcher.ExitCode;
                }

                if (stop.IsCancellationRequested)
                {
                    return 0;
                }

                Console.Error.WriteLine($"basin-mcp: no socket at {launcher.SocketPath} after 30 s; serving anyway");
            }

            bridge = new McpBridge(new McpBridgeOptions
            {
                SocketPath = launcher.SocketPath,
                Filter = filter,
                MaxDimension = maxDimension,
                Reconnect = false,
            });
            var serving = bridge.RunAsync(new StdioServerTransport("basin-mcp"), stop.Token);
            var stopped = Task.Delay(Timeout.Infinite, stop.Token);
            var first = await Task.WhenAny(serving, launcher.Exited, stopped).ConfigureAwait(false);
            if (first == launcher.Exited)
            {
                await stop.CancelAsync().ConfigureAwait(false);
                return launcher.ExitCode;
            }

            if (first == stopped)
            {
                return 0;
            }

            try
            {
                return await serving.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return 0;
            }
        }
        finally
        {
            await launcher.DisposeAsync().ConfigureAwait(false);
            if (bridge is not null)
            {
                await bridge.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static System.Runtime.InteropServices.PosixSignalRegistration Stop(
        CancellationTokenSource stop, System.Runtime.InteropServices.PosixSignal signal) =>
        System.Runtime.InteropServices.PosixSignalRegistration.Create(signal, context =>
        {
            context.Cancel = true;
            stop.Cancel();
        });
}
