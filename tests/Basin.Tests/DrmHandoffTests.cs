using Basin.Backend.Drm;
using Basin.Session;
using Drm;
using Xunit;

namespace Basin.Tests;

public sealed class DrmHandoffTests
{
    private static readonly string? VkmsNode = VkmsCard.Find();

    private sealed class Rig : IDisposable
    {
        public CompositorTestHost Host { get; } = new();

        public DirectSession Session { get; } = new();

        public DrmBackend Backend { get; }

        public DrmOutput Output { get; }

        public DumbAllocator Allocator { get; }

        public List<HandoffInheritance> Inherited { get; } = [];

        public List<HandoffRetention> Retained { get; } = [];

        public int Flips { get; private set; }

        public Rig(bool inherit = true)
        {
            Backend = new DrmBackend(Host.Loop, Session, VkmsNode!) { Inherit = inherit };
            Backend.HandoffRetained += Retained.Add;
            Backend.Start();
            Output = Backend.Outputs.First(o => o.Name.StartsWith("Virtual", StringComparison.Ordinal));
            Output.Frame += () => Flips++;
            Output.HandoffInherited += (_, inheritance) => Inherited.Add(inheritance);
            Allocator = new DumbAllocator(Backend);
        }

        public BufferBase Frame(OutputMode mode) =>
            (BufferBase?)Allocator.Allocate(mode.Width, mode.Height, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear], BufferUse.Scanout)
                ?? throw new InvalidOperationException("vkms refused a dumb buffer");

        public void Light(OutputMode mode, uint fill = 0)
        {
            var frame = Frame(mode);
            try
            {
                if (fill != 0)
                {
                    Fill(frame, fill);
                }

                using var state = new OutputState();
                Assert.True(Output.Commit(state.SetEnabled(true).SetMode(mode).SetBuffer(frame)));
                var deadline = Environment.TickCount64 + 500;
                while (Flips < 1 && Environment.TickCount64 < deadline)
                {
                    Host.Loop.Dispatch(5);
                }

                Assert.Equal(1, Flips);
            }
            finally
            {
                frame.Destroy();
            }
        }

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Allocator.Dispose();
            Backend.Dispose();
            Session.Dispose();
            Host.Dispose();
        }
    }

    private sealed class Holder : IDisposable
    {
        private readonly DirectSession _session = new();
        private readonly ISessionDevice _device;

        public Holder()
        {
            _device = _session.OpenDevice(VkmsNode!);
            using var drm = DrmDevice.FromFd(_device.FileDescriptor, ownsFd: false);
            try
            {
                drm.DropMaster();
            }
            catch (DrmException)
            {
            }
        }

        public void Dispose()
        {
            _device.Dispose();
            _session.Dispose();
        }
    }

    private static unsafe void Fill(BufferBase frame, uint argb)
    {
        Assert.True(frame.BeginDataAccess(BufferDataAccess.Write, out var view));
        try
        {
            for (var y = 0; y < frame.Height; y++)
            {
                var row = new Span<uint>((byte*)view.Data + ((long)view.Stride * y), frame.Width);
                row.Fill(argb);
                if (argb == Halves)
                {
                    row[(frame.Width / 2)..].Fill(y < frame.Height / 2 ? 0xff20c040u : 0xffe0e0e0u);
                    row[..(frame.Width / 2)].Fill(0xff2040a0u);
                }
            }
        }
        finally
        {
            frame.EndDataAccess();
        }
    }

    private const uint Halves = 0xff010203;

    private static unsafe uint PixelAt(IBuffer buffer, int x, int y)
    {
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Read, out var view));
        try
        {
            return *(uint*)((byte*)view.Data + ((long)view.Stride * y) + (x * 4));
        }
        finally
        {
            buffer.EndDataAccess();
        }
    }

    private static void SkipWithoutVkms() => Assert.SkipWhen(VkmsNode is null, "no vkms card node");

    private static Rig LitRig(bool inherit = true)
    {
        using (var first = new Rig())
        {
            first.Light(first.Output.PreferredMode);
        }

        var rig = new Rig(inherit);
        if (rig.Output.Inherited is null)
        {
            rig.Dispose();
            Assert.Skip("vkms left nothing lit after the last close; its fbdev emulation is off");
        }

        return rig;
    }

    [Fact]
    public void A_lit_output_is_inherited_without_a_modeset()
    {
        SkipWithoutVkms();
        using var rig = LitRig();
        var inherited = rig.Output.Inherited!;
        Assert.NotEqual(0u, inherited.FramebufferId);
        Assert.Equal(inherited.OutputMode, rig.Output.StartMode);

        rig.Light(rig.Output.StartMode);

        var only = Assert.Single(rig.Inherited);
        Assert.Equal(HandoffReason.Matched, only.Reason);
        Assert.False(only.Modeset);
        Assert.Null(rig.Output.Inherited);
    }

    [Fact]
    public void A_mode_other_than_the_lit_one_wins_with_one_modeset()
    {
        SkipWithoutVkms();
        using var rig = LitRig();
        var lit = rig.Output.Inherited!.OutputMode;
        var other = rig.Output.Modes.First(m => m.Width != lit.Width || m.Height != lit.Height);

        rig.Light(other);

        var only = Assert.Single(rig.Inherited);
        Assert.Equal(HandoffReason.Config, only.Reason);
        Assert.True(only.Modeset);
        Assert.Equal(other.Width, only.Mode.Width);
    }

    [Fact]
    public void Handoff_off_reads_the_lit_state_but_modesets_from_the_preferred_mode()
    {
        SkipWithoutVkms();
        using var rig = LitRig(inherit: false);
        Assert.Equal(rig.Output.PreferredMode, rig.Output.StartMode);

        rig.Light(rig.Output.StartMode);

        var only = Assert.Single(rig.Inherited);
        Assert.Equal(HandoffReason.Off, only.Reason);
        Assert.True(only.Modeset);
    }

    [Fact]
    public void A_retained_frame_stays_lit_and_the_next_start_inherits_it_without_a_modeset()
    {
        SkipWithoutVkms();
        using var holder = new Holder();
        HandoffRetention retention;
        using (var first = new Rig())
        {
            first.Light(first.Output.PreferredMode);
            first.Backend.ExitMode = DrmExitMode.Retain;
            first.Dispose();
            retention = Assert.Single(first.Retained);
            Assert.True(retention.Retained, retention.Refusal);
            Assert.NotEqual(0u, retention.FramebufferId);

            using var second = new Rig();
            var inherited = second.Output.Inherited;
            Assert.NotNull(inherited);
            Assert.Equal(retention.FramebufferId, inherited.FramebufferId);

            second.Light(second.Output.StartMode);
            var only = Assert.Single(second.Inherited);
            Assert.Equal(HandoffReason.Matched, only.Reason);
            Assert.False(only.Modeset);
        }
    }

    [Fact]
    public void A_kernel_without_closefb_refuses_the_retain_and_restores()
    {
        SkipWithoutVkms();
        using var holder = new Holder();
        using var first = new Rig();
        first.Light(first.Output.PreferredMode);
        first.Backend.CloseFramebuffer = static (_, _) => 25;
        first.Backend.ExitMode = DrmExitMode.Retain;
        first.Dispose();

        var retention = Assert.Single(first.Retained);
        Assert.False(retention.Retained);
        Assert.Equal("closefb errno=25", retention.Refusal);

        using var second = new Rig();
        Assert.NotEqual(retention.FramebufferId, second.Output.Inherited?.FramebufferId ?? 0);
    }

    [Fact]
    public void Restore_is_the_default_exit()
    {
        SkipWithoutVkms();
        using var rig = new Rig();
        Assert.Equal(DrmExitMode.Restore, rig.Backend.ExitMode);
        rig.Light(rig.Output.PreferredMode);
        rig.Dispose();
        Assert.Empty(rig.Retained);
    }

    [Fact]
    public void A_retained_frame_imports_as_a_dmabuf_and_copies_to_memory_with_its_pixels()
    {
        SkipWithoutVkms();
        using var holder = new Holder();
        using (var first = new Rig())
        {
            first.Light(first.Output.PreferredMode, 0xff3366cc);
            first.Backend.ExitMode = DrmExitMode.Retain;
        }

        using var second = new Rig();
        Assert.True(second.Output.TryImportInherited(out var frame, out var reason), reason);
        using (frame)
        {
            var mode = second.Output.Inherited!.OutputMode;
            Assert.Equal(mode.Width, frame.SourceWidth);
            Assert.Equal(mode.Height, frame.SourceHeight);
            Assert.Equal(DrmFormat.Xrgb8888, frame.Format);
            Assert.True(frame.CpuReadable);
            Assert.True(frame.Dmabuf.TryGetDmabuf(out var attributes));
            Assert.Equal(1, attributes.PlaneCount);
            Assert.True(attributes.Fds[0] >= 0);

            var copy = frame.CopyToMemory();
            Assert.NotNull(copy);
            try
            {
                Assert.Equal(0x3366ccu, PixelAt(copy, 0, 0) & 0xffffff);
                Assert.Equal(0x3366ccu, PixelAt(copy, copy.Width - 1, copy.Height - 1) & 0xffffff);
            }
            finally
            {
                copy.Destroy();
            }
        }
    }

    [Fact]
    public void A_dark_output_has_nothing_to_import()
    {
        SkipWithoutVkms();
        using var rig = new Rig();
        if (rig.Output.Inherited is not null)
        {
            rig.Light(rig.Output.StartMode);
        }

        Assert.False(rig.Output.TryImportInherited(out var frame, out var reason));
        Assert.Null(frame);
        Assert.Equal("nothing-lit", reason);
    }

    [Fact]
    public void A_retained_vkms_frame_crossfades_into_the_new_scene_at_the_midpoint()
    {
        SkipWithoutVkms();
        using var holder = new Holder();
        using (var first = new Rig())
        {
            first.Light(first.Output.PreferredMode, Halves);
            first.Backend.ExitMode = DrmExitMode.Retain;
        }

        using var second = new Rig();
        Assert.True(second.Output.TryImportInherited(out var inherited, out var reason), reason);
        var copy = inherited.CopyToMemory();
        inherited.Dispose();
        Assert.NotNull(copy);

        var host = second.Host;
        var window = new Scene.SceneRect(host.Scene.Root, 80, 60, new RenderColor(1f, 0.85f, 0.2f, 1f));
        window.SetPosition(40, 30);
        var transition = new Basin.Host.CrossfadeTransition(TimeSpan.FromMilliseconds(400), static t => t);
        var run = HandoffSource.Run(host, copy, 160, 120, transition, copy.Width, copy.Height);
        try
        {
            transition.Step(run.Frame, 0.5);
            run.Apply();
            host.RenderFrame();
            Golden.AssertMatches(host, "handoff-vkms-midpoint");
        }
        finally
        {
            run.Dispose();
            window.Destroy();
        }
    }
}
