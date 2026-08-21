using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Skia;
using Basin.Render.Vulkan;
using Pixman;
using SkiaSharp;

namespace Basin.UI.Skia;

internal interface IVulkanDrawTarget : IDisposable
{
    SKCanvas Canvas { get; }

    bool IsCpuReadback { get; }

    VulkanDeviceImage? Imported { get; }

    void ReadInto(IBuffer buffer);
}
