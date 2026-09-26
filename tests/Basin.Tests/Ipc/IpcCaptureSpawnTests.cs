using System.Text.Json;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Ipc;
using Basin.Scene;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace Basin.Tests;

public sealed class IpcCaptureSpawnTests
{
    private static JsonElement Parse(string frame) => JsonDocument.Parse(frame).RootElement.Clone();

    private static IpcTestRig SceneRig() => new((services, host) =>
    {
        services.Use(host.Layout);
        services.Use<IScreenCapture>(new SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer });
    });

    private static void Paint(IpcTestRig rig)
    {
        _ = MappedToplevel.Map(rig.Host, rig.Host.Client, 60, 50, 0xFF3366AA);
        rig.Host.PumpToClient();
        rig.Host.RenderFrame();
    }

    [Fact]
    public void Fd_target_carries_the_pixels_the_scene_renders()
    {
        using var rig = SceneRig();
        Paint(rig);
        var peer = rig.Connect();
        var result = Parse(peer.Call("""{"method":"capture/output","params":{"to":"fd"}}""")).GetProperty("result");
        var width = result.GetProperty("width").GetInt32();
        var height = result.GetProperty("height").GetInt32();
        var stride = result.GetProperty("stride").GetInt32();
        Assert.Equal(160, width);
        Assert.Equal(120, height);
        Assert.Single(peer.ReceivedFds);
        var bytes = new byte[stride * height];
        using (var handle = new SafeFileHandle(peer.ReceivedFds[0], ownsHandle: false))
        {
            Assert.Equal(bytes.Length, RandomAccess.Read(handle, bytes, 0));
        }

        var painted = 0;
        for (var y = 0; y < height; y += 7)
        {
            for (var x = 0; x < width; x += 7)
            {
                var pixel = BitConverter.ToUInt32(bytes, (y * stride) + (x * 4)) | 0xFF000000u;
                Assert.Equal(rig.Host.Pixel(x, y), pixel);
                if (pixel == 0xFF3366AA)
                {
                    painted++;
                }
            }
        }

        Assert.True(painted > 0);
    }

    [Fact]
    public void Path_and_inline_targets_write_pngs()
    {
        using var rig = SceneRig();
        Paint(rig);
        var peer = rig.Connect();
        var file = Path.Combine(Path.GetTempPath(), $"basin-ipc-{Environment.ProcessId}.png");
        try
        {
            var written = Parse(peer.Call($$$$"""{"method":"capture/output","params":{"to":{"path":"{{{{file}}}}"}}}"""));
            Assert.Equal(file, written.GetProperty("result").GetProperty("path").GetString());
            var (_, width, height) = PngCodec.Decode(File.ReadAllBytes(file));
            Assert.Equal((160, 120), (width, height));
        }
        finally
        {
            File.Delete(file);
        }

        var relative = Parse(peer.Call("""{"method":"capture/output","params":{"to":{"path":"shot.png"}}}"""));
        Assert.Equal(IpcErrorCodes.InvalidParams, relative.GetProperty("error").GetProperty("code").GetString());

        var inline = Parse(peer.Call("""{"method":"capture/region","params":{"x":0,"y":0,"width":80,"height":60,"to":"inline","scale":0.5}}"""));
        var png = inline.GetProperty("result").GetProperty("png").GetBytesFromBase64();
        var decoded = PngCodec.Decode(png);
        Assert.Equal((40, 30), (decoded.Width, decoded.Height));
        Assert.Empty(peer.ReceivedFds);
    }

    [Fact]
    public void Max_dimension_scales_the_longer_side_and_never_up()
    {
        using var rig = SceneRig();
        Paint(rig);
        var peer = rig.Connect();

        var inline = Parse(peer.Call("""{"method":"capture/output","params":{"to":"inline","max_dimension":80}}""")).GetProperty("result");
        Assert.Equal((80, 60), (inline.GetProperty("width").GetInt32(), inline.GetProperty("height").GetInt32()));
        var decoded = PngCodec.Decode(inline.GetProperty("png").GetBytesFromBase64());
        Assert.Equal((80, 60), (decoded.Width, decoded.Height));

        var tall = Parse(peer.Call("""{"method":"capture/region","params":{"x":0,"y":0,"width":30,"height":120,"to":"inline","max_dimension":40}}""")).GetProperty("result");
        Assert.Equal((10, 40), (tall.GetProperty("width").GetInt32(), tall.GetProperty("height").GetInt32()));

        var small = Parse(peer.Call("""{"method":"capture/output","params":{"to":"inline","max_dimension":1000}}""")).GetProperty("result");
        Assert.Equal((160, 120), (small.GetProperty("width").GetInt32(), small.GetProperty("height").GetInt32()));

        var file = Path.Combine(Path.GetTempPath(), $"basin-ipc-max-{Environment.ProcessId}.png");
        try
        {
            var written = Parse(peer.Call($$$$"""{"method":"capture/output","params":{"to":{"path":"{{{{file}}}}"},"max_dimension":40}}""")).GetProperty("result");
            Assert.Equal((40, 30), (written.GetProperty("width").GetInt32(), written.GetProperty("height").GetInt32()));
            var (_, width, height) = PngCodec.Decode(File.ReadAllBytes(file));
            Assert.Equal((40, 30), (width, height));
        }
        finally
        {
            File.Delete(file);
        }

        var both = Parse(peer.Call("""{"method":"capture/output","params":{"to":"inline","max_dimension":80,"scale":0.5}}"""));
        Assert.Equal(IpcErrorCodes.InvalidParams, both.GetProperty("error").GetProperty("code").GetString());
        var zero = Parse(peer.Call("""{"method":"capture/output","params":{"to":"inline","max_dimension":0}}"""));
        Assert.Equal(IpcErrorCodes.InvalidParams, zero.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Inline_target_above_the_frame_limit_is_too_large()
    {
        using var rig = new IpcTestRig(services => services.Use<IScreenCapture>(new NoiseCapture(3200, 3200)));
        var peer = rig.Connect();
        var reply = Parse(peer.Call("""{"method":"capture/region","params":{"x":0,"y":0,"width":10,"height":10,"to":"inline"}}"""));
        Assert.Equal(IpcErrorCodes.TooLarge, reply.GetProperty("error").GetProperty("code").GetString());
        Assert.True(Parse(peer.Call("""{"method":"ipc/version"}""")).TryGetProperty("result", out _));
    }

    [Fact]
    public void Spawn_seeds_the_environment_and_reaps_the_child()
    {
        var directory = Directory.CreateTempSubdirectory("basin-ipc-spawn-");
        try
        {
            using var rig = new IpcTestRig(session: new IpcSessionInfo { Compositor = "t", WaylandSocket = "wayland-77" });
            var peer = rig.Connect();
            var reply = Parse(peer.Call($$$"""
                {"method":"process/spawn","params":{"argv":["sh","-c","echo $WAYLAND_DISPLAY $EXTRA $PWD > out"],"env":{"EXTRA":"yes"},"cwd":"{{{directory.FullName}}}"}}
                """));
            var pid = reply.GetProperty("result").GetProperty("pid").GetInt32();
            Assert.True(pid > 0);
            var output = Path.Combine(directory.FullName, "out");
            var deadline = Environment.TickCount64 + 5000;
            while (Environment.TickCount64 < deadline && (!File.Exists(output) || File.ReadAllText(output).Length == 0 || Directory.Exists($"/proc/{pid}")))
            {
                rig.Host.Loop.Dispatch(10);
            }

            Assert.Equal($"wayland-77 yes {directory.FullName}", File.ReadAllText(output).Trim());
            Assert.False(Directory.Exists($"/proc/{pid}"));

            var missing = Parse(peer.Call("""{"method":"process/spawn","params":{"argv":["/nonexistent/program"]}}"""));
            Assert.Equal(IpcErrorCodes.Failed, missing.GetProperty("error").GetProperty("code").GetString());
            var empty = Parse(peer.Call("""{"method":"process/spawn","params":{"argv":[]}}"""));
            Assert.Equal(IpcErrorCodes.InvalidParams, empty.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class NoiseCapture(int width, int height) : IScreenCapture
    {
        public bool Supports(in CaptureSource source) => true;

        public bool TryDescribe(in CaptureSource source, out CaptureFormat format)
        {
            format = new CaptureFormat(width, height, DrmFormat.Argb8888);
            return true;
        }

        public unsafe bool Capture(in CaptureSource source, in Box region, IBuffer target)
        {
            if (!target.BeginDataAccess(BufferDataAccess.Write, out var view))
            {
                return false;
            }

            try
            {
                var random = new Random(7);
                random.NextBytes(new Span<byte>((void*)view.Data, view.Stride * target.Height));
                return true;
            }
            finally
            {
                target.EndDataAccess();
            }
        }

        public bool TryCursorState(IOutput output, out CaptureCursorState cursor)
        {
            cursor = default;
            return false;
        }

        public void SetCursor(IBuffer? image, in CaptureCursorState state)
        {
        }

        public void AddDamageObserver(ICaptureDamageObserver observer)
        {
        }

        public void RemoveDamageObserver(ICaptureDamageObserver observer)
        {
        }
    }
}
