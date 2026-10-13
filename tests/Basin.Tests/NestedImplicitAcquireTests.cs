using Basin.Diagnostics;
using Basin.Renderers;
using Xunit;

namespace Basin.Tests;

public sealed class NestedImplicitAcquireTests
{
    private sealed class CollectingSink : IBasinLogSink
    {
        public List<string> Lines { get; } = [];

        public void Write(BasinLogLevel level, string category, ReadOnlySpan<char> message)
        {
            lock (Lines)
            {
                Lines.Add($"{level}:{message}");
            }
        }
    }

    [Theory]
    [InlineData("gl")]
    [InlineData("vulkan")]
    public void Frames_rendered_without_a_parent_syncobj_reach_the_parent_and_warn_about_nothing(string renderer)
    {
        CompositorTestHost.SkipUnlessRunnable(renderer);
        var stack = RendererCatalog.Create(renderer, CompositorTestHost.RenderNodePath);
        var sink = new CollectingSink();
        BasinLog.Sink = sink;
        try
        {
            Assert.SkipWhen(stack.DeviceAllocator is null, $"the {renderer} row built no device allocator");
            var allocator = stack.DeviceAllocator!;
            using var host = new NestedBackendTestHost(NestedParentOptions.Undecorating with
            {
                Dmabuf = true,
                DmabufFormats = allocator.Formats,
                AnswersFrames = true,
            });

            var output = host.CreateOutput();
            host.Pump();
            var usable = allocator.Formats.Intersect(stack.Renderer.DmabufRenderFormats);
            var modifiers = usable.ModifiersOf(DrmFormat.Xrgb8888).ToArray();
            Assert.SkipWhen(modifiers.Length == 0, "no renderable xrgb modifiers");
            var buffers = new IBuffer[2];
            for (var i = 0; i < buffers.Length; i++)
            {
                buffers[i] = allocator.Allocate(output.CurrentMode.Width, output.CurrentMode.Height, DrmFormat.Xrgb8888, modifiers, BufferUse.Render)!;
                Assert.NotNull(buffers[i]);
            }

            var frames = 0;
            output.Frame += () => frames++;
            for (var frame = 0; frame < 60; frame++)
            {
                var target = buffers[frame % 2];
                var pass = stack.Renderer.BeginBufferPass(target, new RenderPassOptions());
                pass.AddRect(new RenderColor(frame / 60f, 0.3f, 0.6f, 1f), new Box(0, 0, target.Width, target.Height));
                Assert.True(pass.Submit());
                using (var state = new OutputState())
                {
                    Assert.True(output.Commit(state.SetBuffer(target)));
                }

                var before = frames;
                for (var round = 0; round < 200 && frames == before; round++)
                {
                    host.Pump(1);
                }

                Assert.True(frames > before, $"frame {frame} never completed");
            }

            foreach (var buffer in buffers)
            {
                ((BufferBase)buffer).Destroy();
            }

            host.Pump();
            lock (sink.Lines)
            {
                Assert.DoesNotContain(sink.Lines, line => line.Contains("presenting without explicit sync", StringComparison.Ordinal));
            }
        }
        finally
        {
            BasinLog.Sink = null;
            stack.DeviceAllocator?.Dispose();
            stack.Renderer.Dispose();
        }
    }
}
