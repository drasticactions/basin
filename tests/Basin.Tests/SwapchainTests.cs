using Xunit;

namespace Basin.Tests;

public sealed class SwapchainTests
{
    [Fact]
    public void Dispose_destroys_every_buffer()
    {
        using var allocator = new ShmAllocator();
        var swapchain = new Swapchain(allocator, 64, 48, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        var buffers = new List<IBuffer>();
        var locks = new List<BufferLock>();
        for (var i = 0; i < 3; i++)
        {
            var buffer = swapchain.Acquire(out _);
            Assert.NotNull(buffer);
            buffers.Add(buffer!);
            locks.Add(buffer!.Lock());
        }

        Assert.Equal(3, buffers.Distinct().Count());
        foreach (var held in locks)
        {
            held.Dispose();
        }

        swapchain.Dispose();

        Assert.All(buffers, buffer => Assert.True(buffer.IsDestroyed));
    }
}
