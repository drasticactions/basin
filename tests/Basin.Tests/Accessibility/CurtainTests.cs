using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Ipc;
using Basin.Scene;
using Xunit;
using static Basin.Tests.IpcApprovalTests;

namespace Basin.Tests;

public sealed class CurtainTests
{
    private static readonly SceneCommitOptions Gray = new() { Background = new RenderColor(0.3f, 0.3f, 0.3f, 1f), AllowPlaneOffload = true };

    private static bool AllBlack(byte[] rgba)
    {
        for (var i = 0; i < rgba.Length; i += 4)
        {
            if (rgba[i] != 0 || rgba[i + 1] != 0 || rgba[i + 2] != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] Pixels(MemoryBuffer buffer) => BufferCapture.ReadRgba(buffer);

    [Fact]
    public void A_drawn_curtain_presents_black_ignores_damage_and_lifts_to_the_scene()
    {
        using var host = new CompositorTestHost();
        var rect = new SceneRect(host.Scene.Root, 40, 30, new RenderColor(1f, 0f, 0f, 1f));
        rect.SetPosition(10, 10);
        var curtain = new SceneCurtain(host.Scene);
        var output = host.SceneOutput;
        using var state = new OutputState();
        Assert.True(output.Commit(host.Renderer, host.Target, 0, state, Gray));
        Assert.Equal(0xFFFF0000u, host.Pixel(20, 20));

        curtain.Set(true);
        Assert.True(output.Commit(host.Renderer, host.Target, 1, state, Gray));
        Assert.True(AllBlack(Pixels(host.Target)));
        Assert.Equal("screen curtain", output.OffloadSkipped);

        rect.SetPosition(60, 60);
        Assert.False(output.Commit(host.Renderer, host.Target, 1, state, Gray));

        curtain.Set(false);
        Assert.True(output.Commit(host.Renderer, host.Target, 1, state, Gray));
        Assert.Equal(0xFFFF0000u, host.Pixel(70, 70));
        Assert.Equal(0xFF4C4C4Cu, host.Pixel(20, 20));
    }

    [Fact]
    public void A_capture_is_black_under_the_curtain_and_sees_through_only_during_a_pass()
    {
        using var host = new CompositorTestHost();
        var rect = new SceneRect(host.Scene.Root, 40, 30, new RenderColor(0f, 0.6f, 1f, 1f));
        rect.SetPosition(10, 10);
        var curtain = new SceneCurtain(host.Scene);
        var capture = new SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer, Background = RenderColor.Black };
        var source = CaptureSource.Output(host.Output);
        Assert.True(capture.TryDescribe(source, out var format));
        var shot = new MemoryBuffer(format.Width, format.Height, DrmFormat.Xrgb8888);
        try
        {
            Assert.True(capture.Capture(source, default, shot));
            var plain = Pixels(shot);
            Assert.False(AllBlack(plain));

            curtain.Set(true);
            Assert.True(host.Scene.HidesCaptures);
            Assert.True(capture.Capture(source, default, shot));
            Assert.True(AllBlack(Pixels(shot)));

            curtain.BeginPass();
            Assert.True(curtain.CapturesSee);
            Assert.True(capture.Capture(source, default, shot));
            Assert.Equal(plain, Pixels(shot));
            curtain.EndPass();
            curtain.EndPass();

            Assert.True(capture.Capture(source, default, shot));
            Assert.True(AllBlack(Pixels(shot)));
        }
        finally
        {
            shot.Destroy();
        }
    }

    [Fact]
    public void An_ipc_capture_under_the_curtain_waits_for_a_person_and_sees_only_when_allowed()
    {
        using var rig = new IpcTestRig(
            (services, host) =>
            {
                services.Use(host.Layout);
                services.Use<IScreenCapture>(new SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer });
            });
        _ = MappedToplevel.Map(rig.Host, rig.Host.Client, 60, 50, 0xFF3366AA);
        rig.Host.PumpToClient();
        rig.Host.RenderFrame();
        var curtain = new SceneCurtain(rig.Host.Scene);
        rig.Server.GuardWithCurtain(curtain);
        var person = rig.Connect();
        var agent = rig.Connect();
        _ = person.Call("""{"method":"ipc/subscribe","params":{"events":["approval/requested"]}}""");
        curtain.Set(true);

        agent.Send("""{"id":1,"method":"capture/output","params":{"to":"inline"}}""");
        var request = Event(person);
        Assert.Equal("capture/output", request.GetProperty("method").GetString());
        Assert.Contains("screen curtain", request.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Null(Code(Parse(person.Call($$$"""{"id":2,"method":"approval/answer","params":{"id":{{{request.GetProperty("id").GetInt64()}}},"answer":"allow_once"}}"""))));
        var allowed = PngCodec.Decode(Reply(agent, 1).GetProperty("result").GetProperty("png").GetBytesFromBase64());
        Assert.False(AllBlack(allowed.Rgba));
        Assert.False(curtain.CapturesSee);

        agent.Send("""{"id":3,"method":"capture/output","params":{"to":"inline"}}""");
        request = Event(person);
        Assert.Null(Code(Parse(person.Call($$$"""{"id":4,"method":"approval/answer","params":{"id":{{{request.GetProperty("id").GetInt64()}}},"answer":"deny"}}"""))));
        Assert.Equal(IpcErrorCodes.Refused, Code(Reply(agent, 3)));
        Assert.False(curtain.CapturesSee);

        curtain.Set(false);
        var lifted = PngCodec.Decode(Parse(agent.Call("""{"id":5,"method":"capture/output","params":{"to":"inline"}}""")).GetProperty("result").GetProperty("png").GetBytesFromBase64());
        Assert.Equal(allowed.Rgba, lifted.Rgba);
    }
}
