using System.Diagnostics;
using System.Text.Json;
using Basin.Ipc;
using Basin.Tests;
using ModelContextProtocol.Protocol;
using Xunit;

namespace BasinMcp.Tests;

public sealed class McpLifecycleTests
{
    private static T Finish<T>(Task<T> task) => task.GetAwaiter().GetResult();

    private static string Text(CallToolResult result) => string.Join('\n', result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static IpcServer Restart(McpHarness harness, string compositor, Action<IpcServer>? register = null)
    {
        var rig = harness.Rig!;
        rig.Server.Dispose();
        harness.PumpUntil(() => harness.Bridge.Client is not { IsConnected: true });
        var server = new IpcServer(rig.Host.Loop, rig.Services, new IpcSessionInfo { Compositor = compositor, Quit = () => { } }, harness.SocketPath, listen: true)
        {
            SyntheticInput = rig.Server.SyntheticInput,
            Approvals = rig.Server.Approvals is null ? null : new IpcApprovalBroker(),
        };
        register?.Invoke(server);
        server.Start();
        return server;
    }

    [Fact]
    public void A_restart_with_a_new_method_set_reconnects_and_sends_one_list_changed()
    {
        using var harness = McpHarness.Full();
        _ = harness.ListTools();
        using var second = Restart(harness, "second", server =>
            server.Methods.Register("test/extra", (ref IpcParams _, IpcReply reply) =>
            {
                reply.Result.WriteStartObject();
                reply.Result.WriteEndObject();
            }));

        var version = harness.Call("ipc_version");
        Assert.NotEqual(true, version.IsError);
        Assert.Equal("second", version.StructuredContent!.Value.GetProperty("compositor").GetString());
        harness.PumpUntil(() => harness.ListChanged > 0);
        harness.PumpUntil(() => false, 300);
        Assert.Equal(1, harness.ListChanged);
        Assert.Contains("test_extra", harness.ListTools().Select(tool => tool.Name));
        Assert.NotEqual(true, harness.Call("test_extra").IsError);
    }

    [Fact]
    public void A_restart_with_the_same_method_set_reconnects_quietly()
    {
        using var harness = McpHarness.Full();
        _ = harness.ListTools();
        using var second = Restart(harness, "same");
        var listed = harness.Call("session_describe");
        Assert.NotEqual(true, listed.IsError);
        Assert.Equal("same", listed.StructuredContent!.Value.GetProperty("compositor").GetString());
        harness.PumpUntil(() => false, 300);
        Assert.Equal(0, harness.ListChanged);
    }

    [Fact]
    public void With_no_compositor_only_the_bridge_tools_list_until_a_socket_appears()
    {
        using var harness = McpHarness.Create(null);
        var tools = harness.ListTools();
        Assert.Equal([McpBridgeStatus.Name, McpWaitEvent.Name], tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));

        var status = harness.Call("bridge_status").StructuredContent!.Value;
        Assert.False(status.GetProperty("connected").GetBoolean());
        Assert.Equal(McpBridgeOptions.DefaultMaxDimension, status.GetProperty("max_dimension").GetInt32());
        Assert.False(status.GetProperty("filters").GetProperty("read_only").GetBoolean());
        var tried = Assert.Single(status.GetProperty("tried").EnumerateArray());
        Assert.Equal(harness.SocketPath, tried.GetProperty("path").GetString());
        Assert.Contains(harness.SocketPath, tried.GetProperty("error").GetString(), StringComparison.Ordinal);

        var unavailable = harness.Call("windows_list");
        Assert.True(unavailable.IsError);
        Assert.StartsWith("unavailable: no compositor: ", Text(unavailable), StringComparison.Ordinal);

        var rig = IpcFullRig.Create(harness.SocketPath, listen: true);
        rig.Server.Start();
        harness.Rig = rig;
        harness.PumpUntil(() => harness.ListChanged > 0);
        Assert.Equal(1, harness.ListChanged);
        Assert.Contains("windows_list", harness.ListTools().Select(tool => tool.Name));
        Assert.True(harness.Call("bridge_status").StructuredContent!.Value.GetProperty("connected").GetBoolean());
    }

    [Fact]
    public void Stdout_carries_only_json_rpc_frames()
    {
        var directory = Directory.CreateTempSubdirectory("basin-mcp-stdout-");
        try
        {
            var path = Path.Combine(directory.FullName, "basin-test.sock");
            using var rig = IpcFullRig.Create(path, listen: true);
            rig.Server.Start();
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "basin-mcp"))
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add("--socket");
            start.ArgumentList.Add(path);
            start.ArgumentList.Add("--log-level");
            start.ArgumentList.Add("trace");
            using var process = Process.Start(start)!;
            var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            string[] requests =
            [
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}""",
                """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
                """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
                """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"windows_list","arguments":{}}}""",
                """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"capture_output","arguments":{}}}""",
                """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"nope","arguments":{}}}""",
            ];
            var lines = new List<string>();
            foreach (var request in requests)
            {
                process.StandardInput.WriteLine(request);
                process.StandardInput.Flush();
                if (!request.Contains("\"id\"", StringComparison.Ordinal))
                {
                    continue;
                }

                var read = process.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken).AsTask();
                var deadline = Environment.TickCount64 + 15_000;
                while (!read.IsCompleted && Environment.TickCount64 < deadline)
                {
                    rig.Host.Loop.Dispatch(5);
                }

                Assert.True(read.IsCompleted, $"no reply to {request}");
                lines.Add(Finish(read)!);
            }

            process.StandardInput.Close();
            var tail = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var exitDeadline = Environment.TickCount64 + 15_000;
            while (!process.HasExited && Environment.TickCount64 < exitDeadline)
            {
                rig.Host.Loop.Dispatch(5);
            }

            Assert.True(process.HasExited);
            var errors = Finish(stderr);
            process.StandardOutput.Dispose();
            process.StandardError.Dispose();
            lines.AddRange(Finish(tail).Split('\n', StringSplitOptions.RemoveEmptyEntries));
            Assert.Equal(5, lines.Count);
            foreach (var line in lines)
            {
                using var frame = JsonDocument.Parse(line);
                Assert.Equal("2.0", frame.RootElement.GetProperty("jsonrpc").GetString());
            }

            Assert.Contains("[info] mcp: connected to", errors, StringComparison.Ordinal);
            Assert.Contains("\"image\"", lines[3], StringComparison.Ordinal);
            Assert.Contains("\"error\"", lines[4], StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
