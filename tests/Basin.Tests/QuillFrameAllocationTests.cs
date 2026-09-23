using Basin.Capabilities;
using Basin.Frames.Quill;
using Basin.UI.Quill;
using Pixman;
using Xunit;

namespace Basin.Tests;

public sealed class QuillFrameAllocationTests
{
    private const int Rounds = 20;

    [Fact]
    public void A_quill_frame_repaint_stays_within_budget() => AssertBudget("gl", "gl", "quill-frame-repaint");

    [Fact]
    public void A_quill_frame_repaint_on_vulkan_stays_within_budget() =>
        AssertBudget("vulkan", "vulkan", "quill-frame-repaint-vk");

    private static void AssertBudget(string row, string backend, string path)
    {
        Budgets.Require();
        CompositorTestHost.SkipUnlessRunnable(row);
        using var host = new CompositorTestHost(renderer: row);
        using var lease = QuillHostTests.QuillLease.Open(host, backend);
        Assert.SkipWhen(lease is null, $"this row builds no {backend} quill host");

        var renderer = new QuillFrameRenderer(new QuillFrameTheme { Face = QuillFrameFonts.Bundled() });
        var state = new FrameState
        {
            Title = "Quill frame repaint",
            AppId = "basin.quill",
            Active = true,
            Capabilities = FrameCapabilities.Maximize | FrameCapabilities.Minimize | FrameCapabilities.WindowMenu,
        };

        var insets = renderer.Measure(state, 1.0);
        var client = new Box(insets.Left, insets.Top, 320, 200);
        var surface = (IQuillUISurface)lease!.Host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Dmabuf,
            Width = client.Width + insets.Left + insets.Right,
            Height = client.Height + insets.Top + insets.Bottom,
            Scale = 1.0,
        })!;

        Repaint(renderer, surface, client, state, Rounds);

        var before = GC.GetAllocatedBytesForCurrentThread();
        Repaint(renderer, surface, client, state, Rounds);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Budgets.Check("server", path, allocated);
        surface.Dispose();
    }

    private static void Repaint(
        QuillFrameRenderer renderer, IQuillUISurface surface, in Box client, in FrameState state, int rounds)
    {
        for (var i = 0; i < rounds; i++)
        {
            var interaction = new FrameInteraction(
                (i & 1) == 0 ? FramePart.Close : FramePart.None, FramePart.None);
            renderer.Draw(surface, client, state, interaction);
            if (surface.TryAcquire(out var frame))
            {
                frame.Dispose();
            }
        }
    }
}
