using Basin.Diagnostics;
using Wayland.Server;
using Wayland.Server.Shm;
using Xunit;

namespace Basin.Tests;

public sealed class TokenShmPoolTests
{
    [Fact]
    public void A_token_backed_pool_serves_buffers_with_no_kernel_fd_in_the_slot_path()
    {
        ServeOneBuffer();

        var baseline = FdSnapshot.Take();
        for (var i = 0; i < 4; i++)
        {
            ServeOneBuffer();
        }

        Assert.Empty(FdSnapshot.Diff(baseline, FdSnapshot.Take()));

        static void ServeOneBuffer()
        {
            var table = new FdSlotTable();
            var shm = new TokenSharedMemory(table);
            var region = new SharedMemoryRegion(4096);
            var span = region.Span;
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = (byte)(i * 31);
            }

            var pool = new ShmPool(shm.Map(table.Mint(region), 4096));
            Assert.Equal(0, table.Count);
            Assert.Equal(4096, pool.Size);
            Assert.True(pool.IsWritable);

            var buffer = new ManagedShmBuffer(pool, 0, 16, 16, 64, DrmFormat.Xrgb8888);
            Assert.True(buffer.BeginDataAccess(BufferDataAccess.Read, out var view));
            unsafe
            {
                Assert.Equal((byte)0, *(byte*)view.Data);
                Assert.Equal((byte)31, *((byte*)view.Data + 1));
            }

            buffer.EndDataAccess();

            buffer.Destroy();
            pool.Release();
            Assert.Throws<ObjectDisposedException>(() => region.Span[0]);
        }
    }

    [Fact]
    public void A_token_pool_resize_keeps_a_reader_on_its_generation()
    {
        var table = new FdSlotTable();
        var shm = new TokenSharedMemory(table);
        var region = new SharedMemoryRegion(4096);
        region.Span.Fill(0xAA);

        var pool = new ShmPool(shm.Map(table.Mint(region), 4096));
        var buffer = new ManagedShmBuffer(pool, 0, 16, 16, 64, DrmFormat.Xrgb8888);
        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Read, out var before));

        region.Grow(8192);
        pool.Resize(8192);
        Assert.Equal(8192, pool.Size);

        Assert.True(buffer.BeginDataAccess(BufferDataAccess.Read, out var after));
        Assert.Equal(before.Data, after.Data);
        unsafe
        {
            Assert.Equal((byte)0xAA, *(byte*)after.Data);
        }

        buffer.EndDataAccess();
        buffer.EndDataAccess();

        var tail = new ManagedShmBuffer(pool, 4096, 16, 16, 64, DrmFormat.Xrgb8888);
        Assert.True(tail.BeginDataAccess(BufferDataAccess.Read, out var grown));
        unsafe
        {
            Assert.Equal((byte)0, *(byte*)grown.Data);
        }

        tail.EndDataAccess();

        tail.Destroy();
        buffer.Destroy();
        pool.Release();
        Assert.Throws<ObjectDisposedException>(() => region.Span[0]);
    }

    [Fact]
    public void Token_pool_bounds_are_checked_in_64_bit()
    {
        var table = new FdSlotTable();
        var shm = new TokenSharedMemory(table);
        var region = new SharedMemoryRegion(4096);

        var pool = new ShmPool(shm.Map(table.Mint(region), 4096));
        Assert.True(pool.TryGetRegion(0, 16, 64, out _));
        Assert.False(pool.TryGetRegion(0, 0x10000, 0x20000, out _));
        Assert.False(pool.TryGetRegion(-1, 16, 64, out _));
        Assert.False(pool.TryGetRegion(4095, 16, 64, out _));

        pool.Release();
    }
}
