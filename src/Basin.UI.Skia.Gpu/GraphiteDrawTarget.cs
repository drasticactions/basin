using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Skia;
using Basin.Render.Vulkan;
using Pixman;
using SkiaSharp;

namespace Basin.UI.Skia;

internal sealed class GraphiteDrawTarget(SkiaGraphiteTarget target) : IVulkanDrawTarget
{
    public SKCanvas Canvas => target.Canvas;

    public bool IsCpuReadback => target.IsCpuReadback;

    public VulkanDeviceImage? Imported => target.Imported;

    public void ReadInto(IBuffer buffer) => target.ReadInto(buffer);

    public void Dispose() => target.Dispose();
}
