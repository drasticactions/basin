using System.Text.Json;
using Basin.Ipc;
using Xunit;

namespace Basin.Tests;

public sealed class IpcSettleTests
{
    private static JsonElement Parse(string frame) => JsonDocument.Parse(frame).RootElement.Clone();

    [Fact]
    public void Settle_answers_at_once_when_nothing_is_pending()
    {
        var settle = new ManualSettle { Presented = 7 };
        using var rig = new IpcTestRig(session: new IpcSessionInfo { Compositor = "test", Settle = settle });
        var peer = rig.Connect();

        var reply = Parse(peer.Call("""{"method":"session/settle"}"""));

        Assert.Equal(7, reply.GetProperty("result").GetProperty("presented").GetInt64());
        Assert.Equal(0, settle.Listeners);
    }

    [Fact]
    public void Settle_waits_for_the_outputs_and_is_overtaken_meanwhile()
    {
        var settle = new ManualSettle { IsSettled = false, Presented = 3 };
        using var rig = new IpcTestRig(session: new IpcSessionInfo { Compositor = "test", Settle = settle });
        var peer = rig.Connect();
        peer.Send("""{"id":"settle","method":"session/settle","params":{"timeout_ms":5000}}""");
        peer.Send("""{"id":"now","method":"ipc/version"}""");
        Assert.Equal("now", Parse(peer.Receive()).GetProperty("id").GetString());
        Assert.Equal(1, settle.Listeners);

        settle.Settle(5);
        var reply = Parse(peer.Receive());

        Assert.Equal("settle", reply.GetProperty("id").GetString());
        Assert.Equal(5, reply.GetProperty("result").GetProperty("presented").GetInt64());
        Assert.Equal(0, settle.Listeners);
    }

    [Fact]
    public void Settle_fails_at_its_timeout_and_is_absent_without_a_source()
    {
        var settle = new ManualSettle { IsSettled = false, Unsettled = "Output HEADLESS-1 has a repaint pending." };
        using (var rig = new IpcTestRig(session: new IpcSessionInfo { Compositor = "test", Settle = settle }))
        {
            var peer = rig.Connect();
            peer.Send("""{"id":1,"method":"session/settle","params":{"timeout_ms":20}}""");
            var deadline = Environment.TickCount64 + 2000;
            while (!peer.HasFrame() && Environment.TickCount64 < deadline)
            {
                rig.Host.Loop.Dispatch(10);
            }

            var reply = Parse(peer.Receive());
            Assert.Equal(IpcErrorCodes.Failed, reply.GetProperty("error").GetProperty("code").GetString());
            Assert.EndsWith("Output HEADLESS-1 has a repaint pending.", reply.GetProperty("error").GetProperty("message").GetString());
            Assert.Equal(0, settle.Listeners);
        }

        using var bare = new IpcTestRig();
        var error = Parse(bare.Connect().Call("""{"method":"session/settle"}""")).GetProperty("error");
        Assert.Equal(IpcErrorCodes.UnknownMethod, error.GetProperty("code").GetString());
    }
}
