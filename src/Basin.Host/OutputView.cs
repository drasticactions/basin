using Basin.Scene;
using Wayland.Server;

namespace Basin.Host;

/// <summary>
/// Holds the per-output state that an <see cref="OutputDriver"/> keeps: the scene output, the scheduler, the swapchain and the layout box.
/// </summary>
public sealed class OutputView
{
    internal OutputView(IOutput output, OutputGlobal global)
    {
        Output = output;
        Global = global;
    }

    /// <summary>
    /// Gets the backend output.
    /// </summary>
    public IOutput Output { get; }

    /// <summary>
    /// Gets the <c>wl_output</c> global that advertises the output to clients.
    /// </summary>
    public OutputGlobal Global { get; }

    /// <summary>
    /// Gets the scene output that renders this output. It is <see langword="null"/> for a secondary output and in full-repaint mode.
    /// </summary>
    public SceneOutput? Scene { get; internal set; }

    /// <summary>
    /// Gets the zoom of a secondary output, which renders without a <see cref="SceneOutput"/>. It is <see langword="null"/> for every other output.
    /// </summary>
    public ZoomView? Zoom { get; internal set; }

    /// <summary>
    /// Gets the color lookup tables of the output. Every path has one: it is <see cref="SceneOutput.Color"/> of <see cref="Scene"/> when that exists.
    /// It is <see langword="null"/> only before the driver wires the view.
    /// </summary>
    public OutputColorTables? Color { get; internal set; }

    /// <summary>
    /// Gets the scheduler that paces repaints. It is <see langword="null"/> in full-repaint mode.
    /// </summary>
    public OutputScheduler? Scheduler { get; internal set; }

    /// <summary>
    /// Gets the swapchain that the output renders into. It is <see langword="null"/> before the first repaint.
    /// </summary>
    public Swapchain? Swapchain { get; internal set; }

    /// <summary>
    /// Gets or sets the allocator for the swapchain. The driver chooses it when it adds the view. A change applies only to a swapchain that is created later.
    /// </summary>
    public IAllocator? Allocator { get; set; }

    /// <summary>
    /// Gets the format modifiers that the swapchain can allocate with.
    /// </summary>
    public ulong[] SwapModifiers { get; internal set; } = [DrmFormatSet.ModifierLinear];

    /// <summary>
    /// Gets the number of frames that the output committed.
    /// </summary>
    public long Rendered { get; internal set; }

    /// <summary>
    /// Gets the mode width in pixels at the last repaint.
    /// </summary>
    public int Width { get; internal set; }

    /// <summary>
    /// Gets the mode height in pixels at the last repaint.
    /// </summary>
    public int Height { get; internal set; }

    /// <summary>
    /// Gets the output scale that the driver last applied.
    /// </summary>
    public double Scale { get; internal set; } = 1;

    /// <summary>
    /// Gets the output transform that the driver last applied.
    /// </summary>
    public OutputTransform Transform { get; internal set; }

    /// <summary>
    /// Gets or sets the output that this output mirrors, or <see langword="null"/> to show its own part of the layout. A secondary output shows the source's last frame scaled to fit.
    /// </summary>
    public OutputView? ReplicaSource { get; set; }

    /// <summary>
    /// Gets a value indicating whether the output is on a secondary card. Such an output renders its part of the scene without a scene output, and readiness and the retain sequence do not wait for it.
    /// </summary>
    public bool IsSecondary { get; internal set; }

    /// <summary>
    /// Gets the output's box in the layout, in logical coordinates.
    /// </summary>
    public Box Box { get; internal set; }

    /// <summary>
    /// Gets the buffer of the last committed frame, or <see langword="null"/> before the first commit.
    /// </summary>
    public IBuffer? LastPresentedBuffer { get; internal set; }

    /// <summary>
    /// Gets the surface that the scene output can scan out directly, or <see langword="null"/> when there is none.
    /// </summary>
    public Surface? ScanoutCandidate { get; internal set; }

    internal int RenderFence { get; set; } = -1;

    internal long RenderStartNanos { get; set; }

    internal bool FrameDoneAtFlip { get; set; }

    internal HandoffRun? Handoff { get; set; }

    internal bool Flatten { get; set; }

    /// <summary>
    /// Gets a value indicating whether a handoff transition runs on the output.
    /// </summary>
    public bool HandoffActive => Handoff is not null;

    /// <summary>
    /// Gets or sets data that the compositor attaches to the output. The driver does not read it.
    /// </summary>
    public object? Tag { get; set; }
}
