using Xunit;

namespace Basin.Tests;

public sealed class SurfaceCommitTests
{
    private static MemoryBuffer NewBuffer(int w = 4, int h = 4) => new(w, h, DrmFormat.Xrgb8888);

    [Fact]
    public void Buffer_lock_transfers_from_pending_to_current()
    {
        using var pending = new SurfaceState();
        using var current = new SurfaceState();
        var buffer = NewBuffer();

        pending.SetBuffer(buffer);
        pending.Committed |= SurfaceStateFields.Buffer;
        Assert.Equal(1, buffer.LockCount);

        SurfaceCommit.Move(pending, current);

        Assert.Same(buffer, current.Buffer);
        Assert.Null(pending.Buffer);
        Assert.Equal(SurfaceStateFields.None, pending.Committed);
        Assert.Equal(1, buffer.LockCount);

        buffer.Destroy();
        current.SetBuffer(null);
        Assert.Equal(0, buffer.LockCount);
    }

    [Fact]
    public void A_release_callback_fires_when_its_own_commit_stops_using_the_buffer()
    {
        using var pending = new SurfaceState();
        using var current = new SurfaceState();
        var first = NewBuffer();
        var second = NewBuffer();
        var released = 0;

        pending.SetBuffer(first);
        pending.BufferRelease = new FrameCallback(_ => released++);
        pending.Committed |= SurfaceStateFields.Buffer | SurfaceStateFields.BufferRelease;
        SurfaceCommit.Move(pending, current);

        Assert.Equal(0, released);
        Assert.NotNull(current.BufferRelease);

        pending.SetBuffer(second);
        pending.Committed |= SurfaceStateFields.Buffer;
        SurfaceCommit.Move(pending, current);
        Assert.Equal(1, released);
        Assert.Null(current.BufferRelease);

        pending.SetBuffer(first);
        pending.Committed |= SurfaceStateFields.Buffer;
        SurfaceCommit.Move(pending, current);
        Assert.Equal(1, released);

        first.Destroy();
        second.Destroy();
        current.SetBuffer(null);
    }

    [Fact]
    public void Release_callbacks_ride_the_commit_they_were_made_in()
    {
        using var pending = new SurfaceState();
        using var current = new SurfaceState();
        var first = NewBuffer();
        var second = NewBuffer();
        var firstFired = 0;
        var secondFired = 0;

        pending.SetBuffer(first);
        pending.BufferRelease = new FrameCallback(_ => firstFired++);
        pending.Committed |= SurfaceStateFields.Buffer | SurfaceStateFields.BufferRelease;
        SurfaceCommit.Move(pending, current);

        pending.SetBuffer(second);
        pending.BufferRelease = new FrameCallback(_ => secondFired++);
        pending.Committed |= SurfaceStateFields.Buffer | SurfaceStateFields.BufferRelease;
        SurfaceCommit.Move(pending, current);

        Assert.Equal(1, firstFired);
        Assert.Equal(0, secondFired);

        first.Destroy();
        second.Destroy();
        current.SetBuffer(null);
    }

    [Fact]
    public void A_discarded_state_cancels_its_release_callback_rather_than_firing_it()
    {
        var fired = 0;
        var buffer = NewBuffer();
        var state = new SurfaceState();
        state.SetBuffer(buffer);
        state.BufferRelease = new FrameCallback(_ => fired++);

        state.Dispose();
        Assert.Equal(0, fired);
        buffer.Destroy();
    }

    [Fact]
    public void Replacing_a_buffer_releases_the_old_one()
    {
        using var pending = new SurfaceState();
        using var current = new SurfaceState();
        var first = NewBuffer();
        var second = NewBuffer();
        var firstReleased = false;
        first.Released += () => firstReleased = true;

        pending.SetBuffer(first);
        pending.Committed |= SurfaceStateFields.Buffer;
        SurfaceCommit.Move(pending, current);
        Assert.False(firstReleased);

        pending.SetBuffer(second);
        pending.Committed |= SurfaceStateFields.Buffer;
        SurfaceCommit.Move(pending, current);

        Assert.True(firstReleased);
        Assert.Same(second, current.Buffer);

        first.Destroy();
        second.Destroy();
        current.SetBuffer(null);
    }

    [Fact]
    public void Null_attach_unmaps()
    {
        using var pending = new SurfaceState();
        using var current = new SurfaceState();
        var buffer = NewBuffer();

        pending.SetBuffer(buffer);
        pending.Committed |= SurfaceStateFields.Buffer;
        SurfaceCommit.Move(pending, current);
        current.UpdateDerivedSize();
        Assert.Equal(4, current.Width);

        pending.SetBuffer(null);
        pending.Committed |= SurfaceStateFields.Buffer;
        SurfaceCommit.Move(pending, current);
        current.UpdateDerivedSize();

        Assert.Null(current.Buffer);
        Assert.Equal(0, current.Width);
        Assert.Equal(0, buffer.LockCount);
        buffer.Destroy();
    }

    [Fact]
    public void Unflagged_fields_do_not_move()
    {
        using var pending = new SurfaceState();
        using var current = new SurfaceState();
        current.Scale = 2;
        current.Transform = OutputTransform.Rotate180;

        pending.Scale = 3;
        pending.Transform = OutputTransform.Flipped;
        pending.OffsetX = 7;
        SurfaceCommit.Move(pending, current);

        Assert.Equal(2, current.Scale);
        Assert.Equal(OutputTransform.Rotate180, current.Transform);
        Assert.Equal(0, current.OffsetX);
    }

    [Fact]
    public void Surface_and_buffer_damage_accumulate_separately()
    {
        using var pending = new SurfaceState();
        using var current = new SurfaceState();

        pending.SurfaceDamage.UnionRect(pending.SurfaceDamage, 0, 0, 10, 10);
        pending.Committed |= SurfaceStateFields.SurfaceDamage;
        SurfaceCommit.Move(pending, current);

        pending.BufferDamage.UnionRect(pending.BufferDamage, 20, 20, 5, 5);
        pending.Committed |= SurfaceStateFields.BufferDamage;
        SurfaceCommit.Move(pending, current);

        Assert.False(current.SurfaceDamage.IsEmpty);
        Assert.False(current.BufferDamage.IsEmpty);
        var surfaceExtents = current.SurfaceDamage.Extents;
        var bufferExtents = current.BufferDamage.Extents;
        Assert.Equal((0, 0, 10, 10), (surfaceExtents.X1, surfaceExtents.Y1, surfaceExtents.X2, surfaceExtents.Y2));
        Assert.Equal((20, 20, 25, 25), (bufferExtents.X1, bufferExtents.Y1, bufferExtents.X2, bufferExtents.Y2));
        Assert.True(pending.SurfaceDamage.IsEmpty);
        Assert.True(pending.BufferDamage.IsEmpty);
    }

    [Fact]
    public void Frame_callbacks_append_in_order_and_leave_pending()
    {
        using var pending = new SurfaceState();
        using var current = new SurfaceState();
        var fired = new List<int>();

        pending.FrameCallbacks.Add(new FrameCallback(_ => fired.Add(1)));
        pending.Committed |= SurfaceStateFields.FrameCallbacks;
        SurfaceCommit.Move(pending, current);

        pending.FrameCallbacks.Add(new FrameCallback(_ => fired.Add(2)));
        pending.Committed |= SurfaceStateFields.FrameCallbacks;
        SurfaceCommit.Move(pending, current);

        Assert.Empty(pending.FrameCallbacks);
        Assert.Equal(2, current.FrameCallbacks.Count);
        foreach (var callback in current.FrameCallbacks)
        {
            callback.Done(0);
        }

        Assert.Equal([1, 2], fired);
        current.FrameCallbacks.Clear();
    }

    [Fact]
    public void Two_stage_move_through_a_cache_preserves_everything()
    {
        using var pending = new SurfaceState();
        using var cached = new SurfaceState();
        using var current = new SurfaceState();
        var buffer = NewBuffer();

        pending.SetBuffer(buffer);
        pending.Scale = 2;
        pending.SurfaceDamage.UnionRect(pending.SurfaceDamage, 1, 1, 2, 2);
        pending.Committed |= SurfaceStateFields.Buffer | SurfaceStateFields.Scale | SurfaceStateFields.SurfaceDamage;

        SurfaceCommit.Move(pending, cached);
        Assert.Null(current.Buffer);

        pending.SurfaceDamage.UnionRect(pending.SurfaceDamage, 5, 5, 2, 2);
        pending.Committed |= SurfaceStateFields.SurfaceDamage;
        SurfaceCommit.Move(pending, cached);

        SurfaceCommit.Move(cached, current);

        Assert.Same(buffer, current.Buffer);
        Assert.Equal(2, current.Scale);
        var extents = current.SurfaceDamage.Extents;
        Assert.Equal((1, 1, 7, 7), (extents.X1, extents.Y1, extents.X2, extents.Y2));
        Assert.Equal(1, buffer.LockCount);

        buffer.Destroy();
        current.SetBuffer(null);
    }

    [Fact]
    public void Input_region_round_trips_infinite_and_finite()
    {
        using var pending = new SurfaceState();
        using var current = new SurfaceState();

        pending.Input.UnionRect(pending.Input, 0, 0, 8, 8);
        pending.InputIsInfinite = false;
        pending.Committed |= SurfaceStateFields.InputRegion;
        SurfaceCommit.Move(pending, current);
        Assert.False(current.InputIsInfinite);

        pending.Input.Clear();
        pending.InputIsInfinite = true;
        pending.Committed |= SurfaceStateFields.InputRegion;
        SurfaceCommit.Move(pending, current);
        Assert.True(current.InputIsInfinite);
    }
}
