using Basin.Backend.Drm;
using Basin.Session;
using Xunit;

namespace Basin.Tests;

public sealed class DrmOutFenceTests
{
    private static readonly string? VkmsNode = VkmsCard.Find();

    [Fact]
    public void The_output_closes_each_out_fence_at_the_next_commit_and_at_teardown()
    {
        Assert.SkipWhen(VkmsNode is null, "no vkms card node");
        using var host = new CompositorTestHost();
        using var session = new DirectSession();
        var backend = new DrmBackend(host.Loop, session, VkmsNode!) { Inherit = false };
        try
        {
            backend.Start();
            var output = backend.Outputs.First(o => o.Name.StartsWith("Virtual", StringComparison.Ordinal));
            var flips = 0;
            output.Frame += () => flips++;
            using var allocator = new DumbAllocator(backend);
            var mode = output.PreferredMode;
            var frames = new[]
            {
                Allocate(allocator, mode),
                Allocate(allocator, mode),
            };
            try
            {
                var fences = new HashSet<int>();
                for (var i = 0; i < 10; i++)
                {
                    var before = flips;
                    using (var state = new OutputState())
                    {
                        state.SetBuffer(frames[i % 2]).RequestOutFence();
                        if (i == 0)
                        {
                            state.SetEnabled(true).SetMode(mode);
                        }

                        Assert.True(output.Commit(state), $"commit {i}");
                    }

                    Assert.True(output.LastOutFenceFd >= 0, $"commit {i} carried no out fence");
                    fences.Add(output.LastOutFenceFd);
                    var deadline = Environment.TickCount64 + 500;
                    while (flips == before && Environment.TickCount64 < deadline)
                    {
                        host.Loop.Dispatch(5);
                    }

                    Assert.True(SyncFiles() <= 1, $"commit {i} holds {SyncFiles()} sync_file fds");
                }

                Assert.True(fences.Count >= 1);
            }
            finally
            {
                foreach (var frame in frames)
                {
                    frame.Destroy();
                }
            }
        }
        finally
        {
            backend.Dispose();
        }

        Assert.Equal(0, SyncFiles());
    }

    private static BufferBase Allocate(DumbAllocator allocator, OutputMode mode) =>
        (BufferBase?)allocator.Allocate(mode.Width, mode.Height, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear], BufferUse.Scanout)
            ?? throw new InvalidOperationException("vkms refused a dumb buffer");

    private static int SyncFiles()
    {
        var count = 0;
        foreach (var fd in Directory.GetFiles("/proc/self/fd"))
        {
            try
            {
                if (new FileInfo(fd).LinkTarget?.Contains("sync_file", StringComparison.Ordinal) == true)
                {
                    count++;
                }
            }
            catch (IOException)
            {
            }
        }

        return count;
    }
}
