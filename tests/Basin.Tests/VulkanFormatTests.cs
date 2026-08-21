using Basin.Render.Vulkan;
using Xunit;

namespace Basin.Tests;

public sealed class VulkanFormatTests
{
    [Fact]
    public void Device_answers_for_the_mandatory_formats()
    {
        CompositorTestHost.SkipUnlessVulkanRunnable();
        using var device = new VulkanDevice(CompositorTestHost.RenderNodePath);

        Assert.True(device.ShmFormats.Contains(DrmFormat.Argb8888));
        Assert.True(device.ShmFormats.Contains(DrmFormat.Xrgb8888));

        Assert.True(device.SampleableFormats.Contains(DrmFormat.Argb8888));
        Assert.True(device.SampleableFormats.Contains(DrmFormat.Xrgb8888));
        Assert.True(device.RenderableFormats.Contains(DrmFormat.Xrgb8888));
    }

    [Fact]
    public void Renderable_modifiers_are_single_plane()
    {
        CompositorTestHost.SkipUnlessVulkanRunnable();
        using var device = new VulkanDevice(CompositorTestHost.RenderNodePath);

        foreach (var format in device.RenderableFormats.Formats)
        {
            foreach (var modifier in device.RenderableFormats.ModifiersOf(format))
            {
                Assert.True(device.TryGetModifierPlaneCount(format, modifier, out var planes));
                Assert.Equal(1u, planes);
            }
        }
    }

    [Fact]
    public void Unknown_formats_get_no_texture()
    {
        CompositorTestHost.SkipUnlessVulkanRunnable();
        using var device = new VulkanDevice(CompositorTestHost.RenderNodePath);

        Assert.False(device.SampleableFormats.Contains((DrmFormat)0x30303052 ));
        Assert.False(device.ShmFormats.Contains((DrmFormat)0x30303052));
    }
}
