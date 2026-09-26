using System.Text.Json;
using Basin.Ipc;
using Xunit;

namespace Basin.Tests;

public sealed class IpcInterceptorTests
{
    private static JsonElement Parse(string frame) => JsonDocument.Parse(frame).RootElement.Clone();

    private static string? ErrorCode(string frame) =>
        Parse(frame).TryGetProperty("error", out var error) ? error.GetProperty("code").GetString() : null;

    [Fact]
    public void An_omitted_method_is_not_listed_and_answers_unknown_method()
    {
        using var rig = IpcFullRig.Create(register: server => server.Omit("process/spawn", "seat/*"));
        var peer = rig.Connect();

        var methods = peer.Call("""{"method":"ipc/methods"}""");
        Assert.DoesNotContain("\"process/spawn\"", methods);
        Assert.DoesNotContain("\"seat/", methods);
        Assert.Contains("\"input/key\"", methods);
        Assert.Equal(IpcErrorCodes.UnknownMethod, ErrorCode(peer.Call("""{"method":"process/spawn","params":{"argv":["true"]}}""")));
        Assert.Equal(IpcErrorCodes.UnknownMethod, ErrorCode(peer.Call("""{"method":"seat/key","params":{"code":30}}""")));
        Assert.True(rig.Server.Methods.IsOmitted("seat/text"));
        using var spare = new IpcServer(rig.Host.Loop, rig.Services, new IpcSessionInfo { Compositor = "spare" }, null, false);
        Assert.Throws<ArgumentException>(() => spare.Omit("Not A Name"));
    }

    [Fact]
    public void Omit_and_the_interceptor_are_set_before_the_server_starts()
    {
        using var rig = IpcFullRig.Create();
        _ = rig.Connect();
        Assert.Throws<InvalidOperationException>(() => rig.Server.Omit("process/spawn"));
        Assert.Throws<InvalidOperationException>(() => rig.Server.Interceptor = new Recorder());
    }

    [Fact]
    public void Deny_answers_refused_and_After_sees_it()
    {
        var recorder = new Recorder { Decide = method => method == "session/describe" ? IpcDecision.Deny("not for agents") : IpcDecision.Allow };
        using var rig = IpcFullRig.Create(register: server => server.Interceptor = recorder);
        var peer = rig.Connect();

        var denied = Parse(peer.Call("""{"method":"session/describe"}"""));
        Assert.Equal(IpcErrorCodes.Refused, denied.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("not for agents", denied.GetProperty("error").GetProperty("message").GetString());
        Assert.Null(ErrorCode(peer.Call("""{"method":"ipc/version"}""")));
        Assert.Equal(IpcErrorCodes.InvalidParams, ErrorCode(peer.Call("""{"method":"windows/get","params":{}}""")));
        Assert.Equal(IpcErrorCodes.UnknownMethod, ErrorCode(peer.Call("""{"method":"nothing/here"}""")));

        Assert.Equal(["before session/describe", "before ipc/version", "before windows/get"], recorder.Before);
        Assert.Equal(["after session/describe refused", "after ipc/version ok", "after windows/get invalid_params"], recorder.After);
        Assert.All(recorder.Clients, id => Assert.Equal(recorder.Clients[0], id));
    }

    [Fact]
    public void A_deferred_call_runs_when_allowed_and_answers_refused_when_denied()
    {
        var recorder = new Recorder { Decide = _ => IpcDecision.Defer, HoldOnDefer = true };
        using var rig = IpcFullRig.Create(register: server => server.Interceptor = recorder);
        var peer = rig.Connect();

        peer.Send("""{"id":1,"method":"ipc/version"}""");
        Assert.False(peer.HasFrame());
        var held = Assert.Single(recorder.Held);
        Assert.Equal("ipc/version", held.Method);
        Assert.Equal(1, rig.Server.HeldCount);
        Assert.Empty(recorder.After);
        held.Allow();
        var allowed = Parse(peer.Receive());
        Assert.Equal(1, allowed.GetProperty("id").GetInt32());
        Assert.True(allowed.TryGetProperty("result", out _));
        Assert.Equal(["after ipc/version ok"], recorder.After);
        Assert.Throws<InvalidOperationException>(held.Allow);

        peer.Send("""{"id":2,"method":"windows/get","params":{"id":1}}""");
        Assert.False(peer.HasFrame());
        recorder.Held[1].Deny("a person said no");
        var denied = Parse(peer.Receive());
        Assert.Equal(IpcErrorCodes.Refused, denied.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("a person said no", denied.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(0, rig.Server.HeldCount);
    }

    [Fact]
    public void A_held_call_whose_method_defers_itself_reports_After_when_it_completes()
    {
        var recorder = new Recorder { Decide = _ => IpcDecision.Defer, HoldOnDefer = true };
        using var rig = IpcFullRig.Create(register: server => server.Interceptor = recorder);
        var peer = rig.Connect();

        peer.Send("""{"method":"windows/wait","params":{"app_id":"never","timeout_ms":1}}""");
        Assert.False(peer.HasFrame());
        recorder.Held[0].Allow();
        Assert.Empty(recorder.After);
        Thread.Sleep(5);
        Assert.Equal(IpcErrorCodes.Failed, ErrorCode(peer.Receive()));
        Assert.Equal(["after windows/wait failed"], recorder.After);
    }

    [Fact]
    public void A_defer_without_a_hold_is_an_internal_error_and_the_server_denies_held_calls_at_dispose()
    {
        var recorder = new Recorder { Decide = _ => IpcDecision.Defer };
        using var rig = IpcFullRig.Create(register: server => server.Interceptor = recorder);
        var peer = rig.Connect();
        Assert.Equal(IpcErrorCodes.Internal, ErrorCode(peer.Call("""{"method":"ipc/version"}""")));

        recorder.HoldOnDefer = true;
        peer.Send("""{"method":"ipc/version"}""");
        Assert.False(peer.HasFrame());
        rig.Server.Dispose();
        Assert.True(recorder.Held[0].IsDone);
        Assert.Contains("after ipc/version refused", recorder.After);
    }

    [Fact]
    public void The_line_front_goes_through_the_interceptor()
    {
        var recorder = new Recorder { Decide = _ => IpcDecision.Deny("no") };
        using var rig = IpcFullRig.Create(register: server => server.Interceptor = recorder);
        rig.Server.Start();
        Assert.Equal(0, UnixSocket.Pair(out var mine, out var theirs));
        var front = rig.Server.StartLineFront(theirs);
        var line = System.Text.Encoding.UTF8.GetBytes("ipc ipc/version {}\n");
        Assert.Equal(line.Length, UnixSocket.Send(mine, line, default));
        for (var i = 0; i < 50 && recorder.Before.Count == 0; i++)
        {
            rig.Pump();
        }

        Assert.Equal(["before ipc/version"], recorder.Before);
        Assert.True(recorder.FromLineFront[0]);
        front.Dispose();
        _ = UnixSocket.Close(mine);
        _ = UnixSocket.Close(theirs);
    }

    private sealed class Recorder : IIpcInterceptor
    {
        public Func<string, IpcDecision> Decide { get; set; } = _ => IpcDecision.Allow;

        public bool HoldOnDefer { get; set; }

        public List<string> Before { get; } = [];

        public List<string> After { get; } = [];

        public List<long> Clients { get; } = [];

        public List<bool> FromLineFront { get; } = [];

        public List<IpcHeldCall> Held { get; } = [];

        IpcDecision IIpcInterceptor.Before(string method, ReadOnlySpan<byte> parameters, IpcCallContext context)
        {
            Before.Add($"before {method}");
            Clients.Add(context.Client.Id);
            FromLineFront.Add(context.FromLineFront);
            var decision = Decide(method);
            if (decision.Kind == IpcDecisionKind.Defer && HoldOnDefer)
            {
                var held = context.Hold();
                Assert.Equal(parameters.ToArray(), held.Parameters.ToArray());
                Held.Add(held);
            }

            return decision;
        }

        void IIpcInterceptor.After(string method, IpcCallOutcome outcome, TimeSpan elapsed, IpcCallContext context)
        {
            Assert.True(elapsed >= TimeSpan.Zero);
            After.Add($"after {method} {outcome.ErrorCode ?? "ok"}");
        }
    }
}
