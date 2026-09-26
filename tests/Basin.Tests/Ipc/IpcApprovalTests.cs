using System.Text.Json;
using Basin.Ipc;
using Xunit;

namespace Basin.Tests;

public sealed class IpcApprovalTests
{
    private static JsonElement Parse(string frame) => JsonDocument.Parse(frame).RootElement.Clone();

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IpcTestPeer, List<JsonElement>> Backlog = [];

    private static JsonElement Next(IpcTestPeer peer, Func<JsonElement, bool> match)
    {
        var backlog = Backlog.GetOrCreateValue(peer);
        for (var i = 0; i < backlog.Count; i++)
        {
            if (match(backlog[i]))
            {
                var found = backlog[i];
                backlog.RemoveAt(i);
                return found;
            }
        }

        for (var i = 0; i < 20; i++)
        {
            var frame = Parse(peer.Receive(1000));
            if (match(frame))
            {
                return frame;
            }

            backlog.Add(frame);
        }

        throw new Xunit.Sdk.XunitException("the expected frame never arrived");
    }

    private static JsonElement Event(IpcTestPeer peer) =>
        Next(peer, f => f.TryGetProperty("event", out var name) && name.GetString() == IpcEventNames.ApprovalRequested).GetProperty("data");

    private static JsonElement Reply(IpcTestPeer peer, int id) =>
        Next(peer, f => f.TryGetProperty("id", out var got) && got.ValueKind == JsonValueKind.Number && got.GetInt32() == id);

    private static string? Code(JsonElement frame) =>
        frame.TryGetProperty("error", out var error) ? error.GetProperty("code").GetString() : null;

    private static IpcTestRig Rig(IpcApprovalBroker broker) => IpcFullRig.Create(register: server =>
    {
        server.Approvals = broker;
        server.Interceptor = new Gate(broker);
    });

    [Fact]
    public void A_request_with_no_one_to_answer_is_denied_at_once()
    {
        var broker = new IpcApprovalBroker();
        using var rig = Rig(broker);
        var peer = rig.Connect();
        var denied = Parse(peer.Call("""{"id":1,"method":"process/spawn","params":{"argv":["true"]}}"""));
        Assert.Equal(IpcErrorCodes.Refused, Code(denied));
        Assert.Contains("approval/requested", denied.GetProperty("error").GetProperty("message").GetString());
        Assert.Empty(broker.Pending);
    }

    [Fact]
    public void Only_the_first_subscriber_answers_and_allow_run_covers_the_rest_of_the_run()
    {
        var broker = new IpcApprovalBroker();
        using var rig = Rig(broker);
        var first = rig.Connect();
        var agent = rig.Connect();
        _ = first.Call("""{"method":"ipc/subscribe","params":{"events":["approval/requested"]}}""");
        _ = agent.Call("""{"method":"ipc/subscribe","params":{"events":["approval/requested"]}}""");

        agent.Send("""{"id":1,"method":"process/spawn","params":{"argv":["true"]}}""");
        var request = Event(first);
        Assert.Equal("process/spawn", request.GetProperty("method").GetString());
        Assert.Equal("true", request.GetProperty("arguments").GetProperty("argv")[0].GetString());
        Assert.Equal("the agent wants to start a program", request.GetProperty("reason").GetString());
        var id = request.GetProperty("id").GetInt64();
        _ = Event(agent);

        agent.Send($$$"""{"id":2,"method":"approval/answer","params":{"id":{{{id}}},"answer":"allow_once"}}""");
        Assert.Equal(IpcErrorCodes.Refused, Code(Reply(agent, 2)));
        Assert.Equal(IpcErrorCodes.NotFound, Code(Parse(first.Call("""{"id":9,"method":"approval/answer","params":{"id":999,"answer":"deny"}}"""))));
        Assert.Null(Code(Parse(first.Call($$$"""{"id":3,"method":"approval/answer","params":{"id":{{{id}}},"answer":"allow_once"}}"""))));
        Assert.True(Reply(agent, 1).GetProperty("result").GetProperty("pid").GetInt32() > 0);

        agent.Send("""{"id":4,"method":"process/spawn","params":{"argv":["true"]}}""");
        id = Event(first).GetProperty("id").GetInt64();
        Assert.Null(Code(Parse(first.Call($$$"""{"id":5,"method":"approval/answer","params":{"id":{{{id}}},"answer":"deny"}}"""))));
        var denied = Reply(agent, 4);
        Assert.Equal(IpcErrorCodes.Refused, Code(denied));
        Assert.Equal("a person denied the call", denied.GetProperty("error").GetProperty("message").GetString());

        agent.Send("""{"id":6,"method":"process/spawn","params":{"argv":["true"]}}""");
        id = Event(first).GetProperty("id").GetInt64();
        Assert.Null(Code(Parse(first.Call($$$"""{"id":7,"method":"approval/answer","params":{"id":{{{id}}},"answer":"allow_run"}}"""))));
        Assert.Null(Code(Reply(agent, 6)));
        Assert.True(broker.IsAllowedForRun("process/spawn"));
        Assert.Null(Code(Reply(agent, 8, "process/spawn", agent)));
    }

    private static JsonElement Reply(IpcTestPeer peer, int id, string method, IpcTestPeer sender)
    {
        sender.Send($$$"""{"id":{{{id}}},"method":"{{{method}}}","params":{"argv":["true"]}}""");
        return Reply(peer, id);
    }

    [Fact]
    public void The_next_subscriber_takes_over_when_the_first_closes()
    {
        var broker = new IpcApprovalBroker();
        using var rig = Rig(broker);
        var first = rig.Connect();
        var second = rig.Connect();
        _ = first.Call("""{"method":"ipc/subscribe","params":{"events":["approval/requested"]}}""");
        _ = second.Call("""{"method":"ipc/subscribe","params":{"events":["approval/requested"]}}""");
        first.Dispose();
        rig.Pump();
        rig.Pump();

        second.Send("""{"id":1,"method":"process/spawn","params":{"argv":["true"]}}""");
        var id = Event(second).GetProperty("id").GetInt64();
        second.Send($$$"""{"id":2,"method":"approval/answer","params":{"id":{{{id}}},"answer":"allow_once"}}""");
        Assert.Null(Code(Reply(second, 2)));
        Assert.Null(Code(Reply(second, 1)));
    }

    [Fact]
    public void An_unanswered_request_times_out_and_a_local_dialog_can_answer()
    {
        var broker = new IpcApprovalBroker(TimeSpan.FromMilliseconds(30));
        using var rig = Rig(broker);
        var watcher = rig.Connect();
        var agent = rig.Connect();
        _ = watcher.Call("""{"method":"ipc/subscribe","params":{"events":["approval/requested"]}}""");
        agent.Send("""{"id":1,"method":"process/spawn","params":{"argv":["true"]}}""");
        Thread.Sleep(40);
        var timedOut = Reply(agent, 1);
        Assert.Equal(IpcErrorCodes.Refused, Code(timedOut));
        Assert.Contains("no one answered", timedOut.GetProperty("error").GetProperty("message").GetString());

        watcher.Dispose();
        rig.Pump();
        broker.Timeout = TimeSpan.FromSeconds(30);
        var answers = new List<IpcApprovalAnswer?>();
        broker.Requested += approval =>
        {
            approval.Answered += done => answers.Add(done.Answer);
            Assert.True(broker.Answer(approval.Id, IpcApprovalAnswer.AllowOnce));
        };
        Assert.Null(Code(Parse(agent.Call("""{"id":2,"method":"process/spawn","params":{"argv":["true"]}}"""))));
        Assert.Equal([IpcApprovalAnswer.AllowOnce], answers);
    }

    [Fact]
    public void The_answer_is_recorded_before_the_held_call_completes()
    {
        var broker = new IpcApprovalBroker();
        var gate = new Gate(broker);
        using var rig = IpcFullRig.Create(register: server =>
        {
            server.Approvals = broker;
            server.Interceptor = gate;
        });
        var person = rig.Connect();
        var agent = rig.Connect();
        _ = person.Call("""{"method":"ipc/subscribe","params":{"events":["approval/requested"]}}""");

        agent.Send("""{"id":1,"method":"process/spawn","params":{"argv":["true"]}}""");
        var id = Event(person).GetProperty("id").GetInt64();
        _ = person.Call($$$"""{"method":"approval/answer","params":{"id":{{{id}}},"answer":"deny"}}""");
        Assert.Equal(IpcErrorCodes.Refused, Code(Reply(agent, 1)));
        Assert.Equal(IpcApprovalAnswer.Deny, gate.AnswerSeenAfter);
    }

    private sealed class Gate(IpcApprovalBroker broker) : IIpcInterceptor
    {
        private IpcApproval? _last;

        public IpcApprovalAnswer? AnswerSeenAfter { get; private set; }

        public IpcDecision Before(string method, ReadOnlySpan<byte> parameters, IpcCallContext context)
        {
            if (method != IpcMethodNames.ProcessSpawn || broker.IsAllowedForRun(method))
            {
                return IpcDecision.Allow;
            }

            _last = broker.Request(context.Hold(), "the agent wants to start a program");
            return IpcDecision.Defer;
        }

        public void After(string method, IpcCallOutcome outcome, TimeSpan elapsed, IpcCallContext context)
        {
            if (method == IpcMethodNames.ProcessSpawn)
            {
                AnswerSeenAfter = _last?.Answer;
            }
        }
    }
}
