using Basin.Backend.Drm;
using Basin.Session;
using Drm;
using Xunit;

namespace Basin.Tests;

public sealed class DrmOutputTests
{
    private static readonly string? VkmsNode = FindVkms();

    private static string? FindVkms()
    {
        if (!Directory.Exists("/dev/dri"))
        {
            return null;
        }

        foreach (var node in Directory.GetFiles("/dev/dri", "card*").OrderBy(p => p, StringComparer.Ordinal))
        {
            try
            {
                using var session = new DirectSession();
                using var device = session.OpenDevice(node);
                using var drm = DrmDevice.FromFd(device.FileDescriptor, ownsFd: false);
                if (drm.GetVersion().Name == "vkms")
                {
                    return node;
                }
            }
            catch (Exception e) when (e is DrmException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
            }
        }

        return null;
    }

    private sealed class Rig : IDisposable
    {
        public CompositorTestHost Host { get; } = new();

        public DirectSession Session { get; } = new();

        public DrmBackend Backend { get; }

        public DrmOutput Output { get; }

        public DumbAllocator Allocator { get; }

        public int Flips { get; private set; }

        public Rig()
        {
            Backend = new DrmBackend(Host.Loop, Session, VkmsNode!);
            Backend.Start();
            Output = Backend.Outputs.First(o => o.Name.StartsWith("Virtual", StringComparison.Ordinal));
            Output.Frame += () => Flips++;
            Allocator = new DumbAllocator(Backend);
        }

        public BufferBase Frame()
        {
            var mode = Output.PreferredMode;
            return (BufferBase?)Allocator.Allocate(mode.Width, mode.Height, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear], BufferUse.Scanout)
                ?? throw new InvalidOperationException("vkms refused a dumb buffer");
        }

        public BufferBase Cursor()
        {
            var (width, height) = Backend.CursorSize;
            return (BufferBase?)Allocator.Allocate(width, height, DrmFormat.Argb8888, [DrmFormatSet.ModifierLinear], BufferUse.Cursor)
                ?? throw new InvalidOperationException("vkms refused a cursor buffer");
        }

        public void Enable(BufferBase frame)
        {
            using var state = new OutputState();
            Assert.True(Output.Commit(state.SetEnabled(true).SetMode(Output.PreferredMode).SetBuffer(frame)));
        }

        public void Commit(BufferBase frame)
        {
            using var state = new OutputState();
            Assert.True(Output.Commit(state.SetBuffer(frame)));
        }

        public void PumpFor(int millis)
        {
            var deadline = Environment.TickCount64 + millis;
            while (Environment.TickCount64 < deadline)
            {
                Host.Loop.Dispatch((int)Math.Max(1, Math.Min(5, deadline - Environment.TickCount64)));
            }
        }

        public void PumpUntilFlips(int count, int millis = 500)
        {
            var deadline = Environment.TickCount64 + millis;
            while (Flips < count && Environment.TickCount64 < deadline)
            {
                Host.Loop.Dispatch(5);
            }

            Assert.Equal(count, Flips);
        }

        public void Dispose()
        {
            Allocator.Dispose();
            Backend.Dispose();
            Session.Dispose();
            Host.Dispose();
        }
    }

    private static void SkipWithoutVkms() => Assert.SkipWhen(VkmsNode is null, "no vkms card node");

    [Fact]
    public void The_first_frame_lights_the_output_and_flips_once()
    {
        SkipWithoutVkms();
        using var rig = new Rig();
        var frame = rig.Frame();
        try
        {
            rig.Enable(frame);
            rig.PumpUntilFlips(1);
            Assert.True(rig.Output.Enabled);
            Assert.True(rig.Output.IsScanningOut(frame));
            rig.PumpFor(60);
            Assert.Equal(1, rig.Flips);
        }
        finally
        {
            frame.Destroy();
        }
    }

    [Fact]
    public void A_frame_committed_behind_a_pending_flip_is_queued_and_reaches_the_screen()
    {
        SkipWithoutVkms();
        using var rig = new Rig();
        var first = rig.Frame();
        var second = rig.Frame();
        var third = rig.Frame();
        try
        {
            rig.Enable(first);
            rig.PumpUntilFlips(1);

            rig.Commit(second);
            rig.Commit(third);
            rig.PumpUntilFlips(3);
            Assert.True(rig.Output.IsScanningOut(third));
            Assert.False(rig.Output.IsScanningOut(first));
        }
        finally
        {
            first.Destroy();
            second.Destroy();
            third.Destroy();
        }
    }

    [Fact]
    public void A_cursor_pushed_to_where_it_already_is_commits_nothing()
    {
        SkipWithoutVkms();
        using var rig = new Rig();
        var frame = rig.Frame();
        var cursor = rig.Cursor();
        try
        {
            rig.Enable(frame);
            rig.PumpUntilFlips(1);

            Assert.True(rig.Output.SetCursor(cursor, 2, 3));
            rig.Output.MoveCursor(40, 50);
            rig.PumpUntilFlips(2, 200);
            rig.PumpFor(60);
            var settled = rig.Flips;

            Assert.False(rig.Output.CursorDirty);
            Assert.True(rig.Output.SetCursor(cursor, 2, 3));
            rig.Output.MoveCursor(40, 50);
            Assert.False(rig.Output.CursorDirty);
            rig.PumpFor(120);
            Assert.Equal(settled, rig.Flips);

            rig.Output.MoveCursor(41, 50);
            rig.PumpUntilFlips(settled + 1, 200);
        }
        finally
        {
            cursor.Destroy();
            frame.Destroy();
        }
    }

    [Fact]
    public void Hiding_a_cursor_that_is_not_shown_commits_nothing()
    {
        SkipWithoutVkms();
        using var rig = new Rig();
        var frame = rig.Frame();
        var next = rig.Frame();
        try
        {
            rig.Enable(frame);
            rig.PumpUntilFlips(1);
            rig.PumpFor(60);

            Assert.True(rig.Output.SetCursor(null, 0, 0));
            Assert.True(rig.Output.SetCursor(null, 0, 0));
            rig.PumpFor(120);
            Assert.Equal(1, rig.Flips);
            Assert.False(rig.Output.CursorDirty);

            rig.Commit(next);
            rig.PumpUntilFlips(2, 200);
            Assert.True(rig.Output.IsScanningOut(next));
        }
        finally
        {
            next.Destroy();
            frame.Destroy();
        }
    }

    [Fact]
    public void Adaptive_sync_is_refused_where_the_connector_cannot_and_switching_it_off_commits_nothing()
    {
        SkipWithoutVkms();
        using var rig = new Rig();
        var frame = rig.Frame();
        try
        {
            rig.Enable(frame);
            rig.PumpUntilFlips(1);
            rig.PumpFor(60);

            using var on = new OutputState();
            Assert.False(rig.Output.Commit(on.SetAdaptiveSync(true)));
            Assert.False(rig.Output.AdaptiveSync);

            using var off = new OutputState();
            Assert.True(rig.Output.Commit(off.SetAdaptiveSync(false)));
            rig.PumpFor(120);
            Assert.Equal(1, rig.Flips);
        }
        finally
        {
            frame.Destroy();
        }
    }

    [Fact]
    public void A_cursor_moved_inside_the_ride_window_waits_for_the_next_frame_commit()
    {
        SkipWithoutVkms();
        using var rig = new Rig();
        var first = rig.Frame();
        var second = rig.Frame();
        var cursor = rig.Cursor();
        try
        {
            rig.Enable(first);
            rig.PumpUntilFlips(1);
            Assert.True(rig.Output.SetCursor(cursor, 0, 0));
            rig.PumpUntilFlips(2, 200);
            rig.PumpFor(60);

            rig.Commit(second);
            rig.Output.MoveCursor(100, 100);
            Assert.True(rig.Output.CursorDirty);
            rig.PumpUntilFlips(3, 200);
            Assert.True(rig.Output.CursorDirty);

            rig.PumpUntilFlips(4, 300);
            Assert.False(rig.Output.CursorDirty);
            rig.PumpFor(120);
            Assert.Equal(4, rig.Flips);
        }
        finally
        {
            cursor.Destroy();
            first.Destroy();
            second.Destroy();
        }
    }

    [Fact]
    public void A_cursor_moved_while_frames_flow_rides_with_them()
    {
        SkipWithoutVkms();
        using var rig = new Rig();
        var frames = new[] { rig.Frame(), rig.Frame() };
        var cursor = rig.Cursor();
        try
        {
            rig.Enable(frames[0]);
            rig.PumpUntilFlips(1);
            Assert.True(rig.Output.SetCursor(cursor, 0, 0));
            rig.PumpUntilFlips(2, 200);
            rig.PumpFor(60);
            var settled = rig.Flips;

            for (var i = 0; i < 12; i++)
            {
                rig.Output.MoveCursor(10 + i, 10 + i);
                rig.Commit(frames[(i + 1) % 2]);
                Assert.False(rig.Output.CursorDirty);
                rig.PumpUntilFlips(settled + i + 1, 200);
            }

            rig.PumpFor(120);
            Assert.Equal(settled + 12, rig.Flips);
        }
        finally
        {
            cursor.Destroy();
            frames[0].Destroy();
            frames[1].Destroy();
        }
    }
}
