using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Desktop;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class ForeignToplevelListTests
{
    [Fact]
    public void Toplevels_are_announced_with_identity_and_closed()
    {
        using var host = new CompositorTestHost();
        var model = new TestToplevelModel();
        using var manager = new ForeignToplevelListManager(host.Display, model);

        var first = MappedToplevel.Map(host, host.Client, color: 0xFF112233);
        var firstId = model.Add(string.Empty, "basin-test", first.ServerToplevel.Surface);

        Basin.Desktop.Protocol.ExtForeignToplevelListV1? list = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "ext_foreign_toplevel_list_v1")
            {
                list = registry.Bind<Basin.Desktop.Protocol.ExtForeignToplevelListV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(list);

        var identifiers = new List<string>();
        var appIds = new List<string>();
        var closed = 0;
        var done = 0;
        list!.Toplevel += (_, e) =>
        {
            var handle = e.Toplevel;
            handle.Identifier += (_, ie) => identifiers.Add(ie.Identifier);
            handle.AppId += (_, ae) => appIds.Add(ae.AppId);
            handle.Done += (_, _) => done++;
            handle.Closed += (_, _) => closed++;
        };
        host.PumpUntil(() => done == 1);

        var second = MappedToplevel.Map(host, host.Client, color: 0xFF445566);
        var secondId = model.Add(string.Empty, "basin-test", second.ServerToplevel.Surface);
        host.PumpUntil(() => done == 2);

        Assert.Equal(2, identifiers.Distinct().Count());
        Assert.All(appIds, appId => Assert.Equal("basin-test", appId));

        model.Remove(secondId);
        second.Toplevel.Dispose();
        second.XdgSurface.Dispose();
        second.Surface.Dispose();
        host.PumpUntil(() => closed == 1);
    }
}

public sealed class ImageCopyCaptureTests
{
    private sealed class Bound
    {
        public required Basin.Desktop.Protocol.ExtOutputImageCaptureSourceManagerV1 OutputSources;
        public required Basin.Desktop.Protocol.ExtForeignToplevelImageCaptureSourceManagerV1 ToplevelSources;
        public required Basin.Desktop.Protocol.ExtImageCopyCaptureManagerV1 Capture;
        public Basin.Desktop.Protocol.ExtForeignToplevelListV1? List;
    }

    private static Bound Bind(CompositorTestHost host, bool withList = false)
    {
        Basin.Desktop.Protocol.ExtOutputImageCaptureSourceManagerV1? outputs = null;
        Basin.Desktop.Protocol.ExtForeignToplevelImageCaptureSourceManagerV1? toplevels = null;
        Basin.Desktop.Protocol.ExtImageCopyCaptureManagerV1? capture = null;
        Basin.Desktop.Protocol.ExtForeignToplevelListV1? list = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            switch (e.Interface)
            {
                case "ext_output_image_capture_source_manager_v1":
                    outputs = registry.Bind<Basin.Desktop.Protocol.ExtOutputImageCaptureSourceManagerV1>(e.Name, 1);
                    break;
                case "ext_foreign_toplevel_image_capture_source_manager_v1":
                    toplevels = registry.Bind<Basin.Desktop.Protocol.ExtForeignToplevelImageCaptureSourceManagerV1>(e.Name, 1);
                    break;
                case "ext_image_copy_capture_manager_v1":
                    capture = registry.Bind<Basin.Desktop.Protocol.ExtImageCopyCaptureManagerV1>(e.Name, 1);
                    break;
                case "ext_foreign_toplevel_list_v1" when withList:
                    list = registry.Bind<Basin.Desktop.Protocol.ExtForeignToplevelListV1>(e.Name, 1);
                    break;
            }
        };
        host.PumpToClient();
        Assert.NotNull(outputs);
        Assert.NotNull(toplevels);
        Assert.NotNull(capture);
        return new Bound { OutputSources = outputs!, ToplevelSources = toplevels!, Capture = capture!, List = list };
    }

    private sealed class SessionEvents
    {
        public (uint W, uint H) BufferSize;
        public List<uint> ShmFormats = [];
        public int Done;
        public bool Stopped;

        public static SessionEvents Attach(Basin.Desktop.Protocol.ExtImageCopyCaptureSessionV1 session)
        {
            var events = new SessionEvents();
            session.BufferSize += (_, e) => events.BufferSize = (e.Width, e.Height);
            session.ShmFormat += (_, e) => events.ShmFormats.Add((uint)e.Format);
            session.Done += (_, _) => events.Done++;
            session.Stopped += (_, _) => events.Stopped = true;
            return events;
        }
    }

    private sealed class FrameEvents
    {
        public Box Damage;
        public bool Ready;
        public bool Failed;
        public uint FailReason;

        public static FrameEvents Attach(Basin.Desktop.Protocol.ExtImageCopyCaptureFrameV1 frame)
        {
            var events = new FrameEvents();
            frame.Damage += (_, e) => events.Damage = new Box(e.X, e.Y, e.Width, e.Height);
            frame.Ready += (_, _) => events.Ready = true;
            frame.Failed += (_, e) =>
            {
                events.Failed = true;
                events.FailReason = (uint)e.Reason;
            };
            return events;
        }
    }

    [Fact]
    public void Output_session_gates_frames_on_damage()
    {
        using var host = new CompositorTestHost();
        using var sources = new ImageCaptureSourceManager(host.Display);
        var capture = new TestScreenCapture(host);
        using var manager = new ImageCopyCaptureManager(host.Display, host.Buffers, capture);

        var rect = new Basin.Scene.SceneRect(host.Scene.Root, 40, 30, new RenderColor(1, 0, 0, 1));
        rect.SetPosition(10, 20);

        var bound = Bind(host);
        var source = bound.OutputSources.CreateSource(host.Client.Outputs[0]);
        var session = bound.Capture.CreateSession(source, 0);
        var sessionEvents = SessionEvents.Attach(session);
        host.PumpUntil(() => sessionEvents.Done == 1);
        Assert.Equal((160u, 120u), sessionEvents.BufferSize);
        Assert.Contains(0u, sessionEvents.ShmFormats);
        Assert.Contains(1u, sessionEvents.ShmFormats);

        var frame = session.CreateFrame();
        var frameEvents = FrameEvents.Attach(frame);
        var target = host.Client.CreateBuffer(160, 120, Fill.Solid(160, 120, 0x00000000));
        frame.AttachBuffer(target.Proxy);
        frame.DamageBuffer(0, 0, 160, 120);
        frame.Capture();
        host.PumpUntil(() => frameEvents.Ready || frameEvents.Failed);
        Assert.True(frameEvents.Ready);
        Assert.Equal(new Box(0, 0, 160, 120), frameEvents.Damage);
        unsafe
        {
            var pixel = *(uint*)(target.Data + 25 * target.Stride + 15 * 4);
            Assert.Equal(0xFFFF0000u, pixel | 0xFF000000u);
        }

        frame.Dispose();
        host.PumpToServer();

        var second = session.CreateFrame();
        var secondEvents = FrameEvents.Attach(second);
        second.AttachBuffer(target.Proxy);
        second.DamageBuffer(0, 0, 160, 120);
        second.Capture();
        host.PumpToClient();
        Assert.False(secondEvents.Ready);

        capture.Damage(host.Output, new Box(3, 4, 20, 10));
        host.PumpUntil(() => secondEvents.Ready);
        Assert.Equal(new Box(3, 4, 20, 10), secondEvents.Damage);
        second.Dispose();

        session.Destroy();
        host.PumpToServer();
    }

    [Fact]
    public void Session_counts_are_reported_per_source()
    {
        using var host = new CompositorTestHost();
        using var sources = new ImageCaptureSourceManager(host.Display);
        var capture = new TestScreenCapture(host);
        using var manager = new ImageCopyCaptureManager(host.Display, host.Buffers, capture);

        var counts = new List<(CaptureSourceKind Kind, int Count)>();
        manager.SessionCountChanged += (source, count) => counts.Add((source.Kind, count));

        var bound = Bind(host);
        var first = bound.Capture.CreateSession(bound.OutputSources.CreateSource(host.Client.Outputs[0]), 0);
        var firstEvents = SessionEvents.Attach(first);
        host.PumpUntil(() => firstEvents.Done == 1);
        Assert.Equal((CaptureSourceKind.Output, 1), counts[^1]);

        var second = bound.Capture.CreateSession(bound.OutputSources.CreateSource(host.Client.Outputs[0]), 0);
        var secondEvents = SessionEvents.Attach(second);
        host.PumpUntil(() => secondEvents.Done == 1);
        Assert.Equal((CaptureSourceKind.Output, 2), counts[^1]);

        second.Destroy();
        host.PumpToServer();
        Assert.Equal((CaptureSourceKind.Output, 1), counts[^1]);

        first.Destroy();
        host.PumpToServer();
        Assert.Equal((CaptureSourceKind.Output, 0), counts[^1]);
    }

    [Fact]
    public void Toplevel_session_renders_only_the_window_subtrees()
    {
        using var host = new CompositorTestHost();
        var model = new TestToplevelModel();
        using var list = new ForeignToplevelListManager(host.Display, model);
        using var sources = new ImageCaptureSourceManager(host.Display);
        var capture = new Basin.Scene.SceneScreenCapture(host.Scene, host.Layout)
        {
            Renderer = host.Renderer,
            Toplevels = model,
        };
        using var manager = new ImageCopyCaptureManager(host.Display, host.Buffers, capture);

        var window = MappedToplevel.Map(host, host.Client, width: 60, height: 50, color: 0xFF00FF00);
        foreach (var stray in host.SurfaceScenes.ToArray())
        {
            stray.Destroy();
        }

        host.SurfaceScenes.Clear();
        var tree = new Basin.Scene.SceneTree(host.Scene.Root);
        var scene = new Basin.Scene.SceneSurface(tree, window.ServerToplevel.Surface);

        var toplevelId = model.Add(string.Empty, "basin-test", window.ServerToplevel.Surface, new Box(0, 0, 60, 50));
        capture.ToplevelContent = id =>
            id == toplevelId ? new Basin.Scene.ToplevelCaptureTrees(tree, null) : default;

        var intruder = new Basin.Scene.SceneRect(host.Scene.Root, 60, 50, new RenderColor(1, 0, 0, 1));
        intruder.SetPosition(30, 0);

        var bound = Bind(host, withList: true);
        Basin.Desktop.Protocol.ExtForeignToplevelHandleV1? handle = null;
        bound.List!.Toplevel += (_, e) => handle = e.Toplevel;
        host.PumpUntil(() => handle is not null);

        var source = bound.ToplevelSources.CreateSource(handle!);
        var session = bound.Capture.CreateSession(source, 0);
        var sessionEvents = SessionEvents.Attach(session);
        host.PumpUntil(() => sessionEvents.Done == 1);
        Assert.Equal((60u, 50u), sessionEvents.BufferSize);

        var frame = session.CreateFrame();
        var frameEvents = FrameEvents.Attach(frame);
        var target = host.Client.CreateBuffer(60, 50, Fill.Solid(60, 50, 0x00000000));
        frame.AttachBuffer(target.Proxy);
        frame.DamageBuffer(0, 0, 60, 50);
        frame.Capture();
        host.PumpUntil(() => frameEvents.Ready || frameEvents.Failed);
        Assert.True(frameEvents.Ready);
        unsafe
        {
            var inside = *(uint*)(target.Data + 10 * target.Stride + 10 * 4);
            Assert.Equal(0xFF00FF00u, inside | 0xFF000000u);
            var overlapped = *(uint*)(target.Data + 10 * target.Stride + 40 * 4);
            Assert.Equal(0xFF00FF00u, overlapped | 0xFF000000u);
        }

        frame.Dispose();
        session.Destroy();
        host.PumpToServer();
        scene.Destroy();
        tree.Destroy();
        intruder.Destroy();
    }

    [Fact]
    public void Toplevel_session_captures_window_content()
    {
        using var host = new CompositorTestHost();
        var model = new TestToplevelModel();
        using var list = new ForeignToplevelListManager(host.Display, model);
        using var sources = new ImageCaptureSourceManager(host.Display);
        using var manager = new ImageCopyCaptureManager(host.Display, host.Buffers, new ToplevelCapture(host));

        var window = MappedToplevel.Map(host, host.Client, width: 60, height: 50, color: 0xFF00FF00);
        var toplevelId = model.Add(string.Empty, "basin-test", window.ServerToplevel.Surface, new Box(0, 0, 60, 50));
        var bound = Bind(host, withList: true);
        Assert.NotNull(bound.List);

        Basin.Desktop.Protocol.ExtForeignToplevelHandleV1? handle = null;
        bound.List!.Toplevel += (_, e) => handle = e.Toplevel;
        host.PumpUntil(() => handle is not null);

        var source = bound.ToplevelSources.CreateSource(handle!);
        var session = bound.Capture.CreateSession(source, 0);
        var sessionEvents = SessionEvents.Attach(session);
        host.PumpUntil(() => sessionEvents.Done == 1);
        Assert.Equal((60u, 50u), sessionEvents.BufferSize);

        var frame = session.CreateFrame();
        var frameEvents = FrameEvents.Attach(frame);
        var target = host.Client.CreateBuffer(60, 50, Fill.Solid(60, 50, 0x00000000));
        frame.AttachBuffer(target.Proxy);
        frame.DamageBuffer(0, 0, 60, 50);
        frame.Capture();
        host.PumpUntil(() => frameEvents.Ready || frameEvents.Failed);
        Assert.True(frameEvents.Ready);
        unsafe
        {
            var pixel = *(uint*)(target.Data + 10 * target.Stride + 10 * 4);
            Assert.Equal(0xFF00FF00u, pixel | 0xFF000000u);
        }

        frame.Dispose();
        session.Destroy();
        host.PumpToServer();
    }
}

public sealed class ExportDmabufTests
{
    [DllImport("libc", SetLastError = true)]
    private static extern int memfd_create(string name, uint flags);

    [DllImport("libc")]
    private static extern int ftruncate(int fd, long length);

    [DllImport("libc")]
    private static extern int close(int fd);

    [Fact]
    public void Exports_planes_and_ready_or_cancels()
    {
        using var host = new CompositorTestHost();
        var fd = memfd_create("export-test", 0);
        Assert.True(fd >= 0);
        Assert.Equal(0, ftruncate(fd, 4096));
        var holder = new DmabufAttributes[1];
        holder[0] = new DmabufAttributes
        {
            Width = 160,
            Height = 120,
            Format = DrmFormat.Xrgb8888,
            Modifier = DrmFormatSet.ModifierLinear,
            PlaneCount = 1,
        };
        holder[0].Fds[0] = fd;
        holder[0].Offsets[0] = 0;
        holder[0].Strides[0] = 640;

        var dmabuf = new TestDmabufCapture { Frame = holder[0] };
        using var manager = new ExportDmabufManager(host.Display, dmabuf);

        Basin.Desktop.Protocol.ZwlrExportDmabufManagerV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwlr_export_dmabuf_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.ZwlrExportDmabufManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);

        var frame = proxy!.CaptureOutput(0, host.Client.Outputs[0]);
        (uint W, uint H, uint Format, uint Objects) header = default;
        var objectFds = new List<(uint Index, int Fd, uint Size, uint Stride)>();
        var ready = false;
        var canceled = false;
        frame.Frame += (_, e) => header = (e.Width, e.Height, e.Format, e.NumObjects);
        frame.Object += (_, e) => objectFds.Add((e.Index, e.Fd, e.Size, e.Stride));
        frame.Ready += (_, _) => ready = true;
        frame.Cancel += (_, _) => canceled = true;
        host.PumpUntil(() => ready || canceled);

        Assert.True(ready);
        Assert.Equal((160u, 120u, (uint)DrmFormat.Xrgb8888, 1u), header);
        var plane = Assert.Single(objectFds);
        Assert.Equal(0u, plane.Index);
        Assert.Equal(4096u, plane.Size);
        Assert.Equal(640u, plane.Stride);
        Assert.True(plane.Fd >= 0);
        close(plane.Fd);
        frame.Dispose();

        dmabuf.Frame = null;
        var second = proxy.CaptureOutput(0, host.Client.Outputs[0]);
        var secondCanceled = false;
        second.Cancel += (_, _) => secondCanceled = true;
        host.PumpUntil(() => secondCanceled);
        Assert.True(secondCanceled);
        second.Dispose();

        close(fd);
        host.PumpToServer();
    }
}
