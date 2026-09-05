using Pixman;

namespace Basin;

public readonly record struct RenderPassOptions
{
    public RenderPassOptions()
    {
    }

    public int WaitFenceFd { get; init; } = -1;

    public int SignalFenceFd { get; init; } = -1;

    public Capabilities.ImageDescription? ColorDescription { get; init; } = null;
}
