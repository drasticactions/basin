using System.Text.Json;
using Basin.Ipc;
using Xunit;

namespace Basin.Tests;

public sealed class IpcProcessTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("basin-process-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static JsonElement Parse(string frame) => JsonDocument.Parse(frame).RootElement.Clone();

    private static string? ErrorCode(string frame) =>
        Parse(frame).TryGetProperty("error", out var error) ? error.GetProperty("code").GetString() : null;

    private static JsonElement Exited(IpcTestPeer peer)
    {
        var frame = Parse(peer.Receive(5000));
        Assert.Equal("process/exited", frame.GetProperty("event").GetString());
        return frame.GetProperty("data");
    }

    private static string Json(string text) => "\"" + System.Text.Encodings.Web.JavaScriptEncoder.Default.Encode(text) + "\"";

    [Fact]
    public void A_launch_is_listed_reports_its_exit_and_keeps_its_log()
    {
        using var rig = IpcFullRig.Create();
        var peer = rig.Connect();
        _ = peer.Call("""{"method":"ipc/subscribe","params":{"events":["process/exited"]}}""");
        var log = Path.Combine(_directory, "one.log");

        var spawned = Parse(peer.Call($$$"""{"method":"process/spawn","params":{"argv":["sh","-c","echo one; echo two >&2; echo three; exit 3"],"log":{{{Json(log)}}}}}""")).GetProperty("result");
        var launch = spawned.GetProperty("launch_id").GetInt64();
        Assert.True(spawned.GetProperty("pid").GetInt32() > 0);

        var exited = Exited(peer);
        Assert.Equal(launch, exited.GetProperty("launch_id").GetInt64());
        Assert.Equal(3, exited.GetProperty("exit_code").GetInt32());

        var listed = Parse(peer.Call("""{"method":"process/list"}""")).GetProperty("result").GetProperty("processes");
        var entry = Assert.Single(listed.EnumerateArray());
        Assert.False(entry.GetProperty("running").GetBoolean());
        Assert.Equal(["sh", "-c", "echo one; echo two >&2; echo three; exit 3"], entry.GetProperty("argv").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(log, entry.GetProperty("log").GetString());

        var tail = Parse(peer.Call($$$"""{"method":"process/log","params":{"launch_id":{{{launch}}},"lines":2}}""")).GetProperty("result");
        Assert.Equal(["two", "three"], tail.GetProperty("lines").EnumerateArray().Select(l => l.GetString()));
        Assert.Equal(IpcErrorCodes.NotFound, ErrorCode(peer.Call("""{"method":"process/log","params":{"launch_id":999}}""")));
        Assert.Equal(IpcErrorCodes.InvalidParams, ErrorCode(peer.Call("""{"method":"process/spawn","params":{"argv":["true"],"log":"relative.log"}}""")));
    }

    [Fact]
    public void Kill_terminates_the_group_and_escalates_when_the_term_is_ignored()
    {
        using var rig = IpcFullRig.Create();
        var peer = rig.Connect();
        _ = peer.Call("""{"method":"ipc/subscribe","params":{"events":["process/exited"]}}""");

        var polite = Parse(peer.Call("""{"method":"process/spawn","params":{"argv":["sleep","30"]}}""")).GetProperty("result").GetProperty("launch_id").GetInt64();
        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"process/kill","params":{"launch_id":{{{polite}}},"grace_ms":2000}}""")));
        var exited = Exited(peer);
        Assert.Equal(polite, exited.GetProperty("launch_id").GetInt64());
        Assert.Equal(15, exited.GetProperty("signal").GetInt32());

        var stubborn = Parse(peer.Call("""{"method":"process/spawn","params":{"argv":["sh","-c","trap '' TERM; sleep 30 & wait; sleep 30"]}}""")).GetProperty("result").GetProperty("launch_id").GetInt64();
        Thread.Sleep(100);
        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"process/kill","params":{"launch_id":{{{stubborn}}},"grace_ms":100}}""")));
        exited = Exited(peer);
        Assert.Equal(stubborn, exited.GetProperty("launch_id").GetInt64());
        Assert.Equal(9, exited.GetProperty("signal").GetInt32());

        Assert.Equal(IpcErrorCodes.NotFound, ErrorCode(peer.Call("""{"method":"process/kill","params":{"launch_id":999}}""")));
    }

    [Fact]
    public void Unset_env_removes_a_variable_and_env_adds_one()
    {
        using var rig = IpcFullRig.Create();
        var peer = rig.Connect();
        _ = peer.Call("""{"method":"ipc/subscribe","params":{"events":["process/exited"]}}""");
        var log = Path.Combine(_directory, "env.log");
        var launch = Parse(peer.Call($$$"""{"method":"process/spawn","params":{"argv":["sh","-c","echo ${HOME:-unset} $BASIN_TEST_VALUE"],"unset_env":["HOME"],"env":{"BASIN_TEST_VALUE":"set"},"log":{{{Json(log)}}}}}"""))
            .GetProperty("result").GetProperty("launch_id").GetInt64();
        _ = Exited(peer);
        var tail = Parse(peer.Call($$$"""{"method":"process/log","params":{"launch_id":{{{launch}}}}}""")).GetProperty("result");
        Assert.Equal(["unset set"], tail.GetProperty("lines").EnumerateArray().Select(l => l.GetString()));
    }

    [Fact]
    public void The_tracker_ends_its_children_when_the_consumer_asks_and_never_on_its_own()
    {
        using var rig = IpcFullRig.Create();
        var tracker = rig.Server.Processes;
        var process = tracker.Spawn(new IpcLaunch(["sleep", "30"]), out var error);
        Assert.NotNull(process);
        Assert.Null(error);
        Assert.True(process.IsRunning);
        tracker.TerminateAll(TimeSpan.FromSeconds(2));
        Assert.False(process.IsRunning);
        Assert.Equal(15, process.Signal);
    }
}
