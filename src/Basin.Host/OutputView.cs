using Basin.Scene;
using Wayland.Server;

namespace Basin.Host;

public sealed class OutputView
{
    internal OutputView(IOutput output, OutputGlobal global)
    {
        Output = output;
        Global = global;
    }

    public IOutput Output { get; }

    public OutputGlobal Global { get; }

    public SceneOutput? Scene { get; internal set; }

    public OutputScheduler? Scheduler { get; internal set; }

    public Swapchain? Swapchain { get; internal set; }

    public IAllocator? Allocator { get; set; }

    public ulong[] SwapModifiers { get; internal set; } = [DrmFormatSet.ModifierLinear];

    public long Rendered { get; internal set; }

    public int Width { get; internal set; }

    public int Height { get; internal set; }

    public double Scale { get; internal set; } = 1;

    public OutputTransform Transform { get; internal set; }

    public OutputView? ReplicaSource { get; set; }

    public bool IsSecondary { get; internal set; }

    public Box Box { get; internal set; }

    public IBuffer? LastPresentedBuffer { get; internal set; }

    public object? Tag { get; set; }
}
