using System.Text.Json;
using Basin.Ipc;
using ModelContextProtocol.Protocol;
using Xunit;

namespace BasinMcp.Tests;

public sealed class McpApprovalTests
{
    private static string Text(CallToolResult result) => string.Join('\n', result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static void Gated(IpcServer server)
    {
        var broker = new IpcApprovalBroker(TimeSpan.FromSeconds(10));
        server.Approvals = broker;
        server.Interceptor = new Gate(broker);
    }

    [Fact]
    public void An_approval_becomes_an_elicitation_and_its_answer_decides_the_call()
    {
        var asked = new List<string>();
        var answer = "allow_once";
        using var harness = McpHarness.Full(register: Gated, elicit: request =>
        {
            asked.Add(request!.Message);
            using var document = JsonDocument.Parse($$"""{"answer":"{{answer}}"}""");
            return new ElicitResult
            {
                Action = "accept",
                Content = document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()),
            };
        });
        _ = harness.ListTools();

        var allowed = harness.Call("process_spawn", """{"argv":["true"]}""");
        Assert.NotEqual(true, allowed.IsError);
        Assert.True(allowed.StructuredContent!.Value.GetProperty("pid").GetInt32() > 0);
        var message = Assert.Single(asked);
        Assert.Contains("process/spawn", message);
        Assert.Contains("the agent wants to start a program", message);

        answer = "deny";
        var denied = harness.Call("process_spawn", """{"argv":["true"]}""");
        Assert.True(denied.IsError);
        Assert.Contains("a person denied the call", Text(denied));
        Assert.Equal(2, asked.Count);
        Assert.True(harness.Bridge.Relay.IsSubscribed);
    }

    [Fact]
    public void A_host_without_elicitation_leaves_the_broker_to_deny()
    {
        using var harness = McpHarness.Full(register: Gated);
        _ = harness.ListTools();
        var denied = harness.Call("process_spawn", """{"argv":["true"]}""");
        Assert.True(denied.IsError);
        Assert.Contains("approval/requested", Text(denied));
        Assert.False(harness.Bridge.Relay.IsSubscribed);
    }

    private sealed class Gate(IpcApprovalBroker broker) : IIpcInterceptor
    {
        public IpcDecision Before(string method, ReadOnlySpan<byte> parameters, IpcCallContext context)
        {
            if (method != IpcMethodNames.ProcessSpawn || broker.IsAllowedForRun(method))
            {
                return IpcDecision.Allow;
            }

            _ = broker.Request(context.Hold(), "the agent wants to start a program");
            return IpcDecision.Defer;
        }

        public void After(string method, IpcCallOutcome outcome, TimeSpan elapsed, IpcCallContext context)
        {
        }
    }
}
