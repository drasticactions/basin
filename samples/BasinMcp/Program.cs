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

            var options = new McpBridgeOptions
            {
                SocketPath = result.GetValue(socket),
                Filter = filter,
                MaxDimension = result.GetValue(maxDimension),
            };
            var bridge = new McpBridge(options);
            try
            {
                return bridge.RunAsync(new StdioServerTransport("basin-mcp"), CancellationToken.None).GetAwaiter().GetResult();
            }
            finally
            {
                bridge.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }
}
