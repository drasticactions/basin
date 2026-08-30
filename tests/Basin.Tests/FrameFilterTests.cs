using Basin.Rashader;
using Basin.Rashader.Gl;
using Basin.Rashader.Vulkan;
using Basin.Render.Gl;
using Basin.Render.Vulkan;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class FrameFilterTests
{
    private sealed class UnrunnableFilter : IFrameFilter
    {
        public bool IsSupported => true;
    }

    private sealed class DecliningVulkanFilter : IVulkanFilter
    {
        public int Calls;

        public bool IsSupported => true;

        public bool Record(in VulkanFilterContext context)
        {
            Calls++;
            return false;
        }
    }

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "shaders", name);

    private static void SkipWithoutLibrashader() =>
        Assert.SkipWhen(!RashaderLibrary.IsAvailable(out var whyNot), whyNot ?? "librashader absent");

    private static byte[] ReadRgba(MemoryBuffer buffer) => Basin.Diagnostics.BufferCapture.ReadRgba(buffer);

    [Fact]
    public void A_filter_the_renderer_cannot_run_leaves_composition_unchanged()
    {
        using var host = new CompositorTestHost();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();

        _ = new SceneRect(host.Scene.Root, 160, 120, new RenderColor(1f, 0f, 0f, 1f));
        sceneOutput.SetFrameFilter(new UnrunnableFilter());

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state, new SceneCommitOptions { AllowDirectScanout = false }));
        var rgba = ReadRgba((MemoryBuffer)state.Buffer!);
        Assert.True(rgba[(((60 * 160) + 80) * 4) + 0] > 200, "the scene composes without the filter");
    }

    [Fact]
    public void A_declined_record_falls_back_to_a_plain_blit()
    {
        CompositorTestHost.SkipUnlessRunnable("vulkan");
        using var host = new CompositorTestHost(renderer: "vulkan");
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var state = new OutputState();

        _ = new SceneRect(host.Scene.Root, 160, 120, new RenderColor(1f, 0f, 0f, 1f));
        var filter = new DecliningVulkanFilter();
        sceneOutput.SetFrameFilter(filter);

        var target = new MemoryBuffer(160, 120, DrmFormat.Xrgb8888);
        Assert.True(sceneOutput.Commit(host.Renderer, target, 0, state, new SceneCommitOptions { AllowDirectScanout = false }));
        Assert.Equal(1, filter.Calls);
        var rgba = ReadRgba(target);
        Assert.True(rgba[(((60 * 160) + 80) * 4) + 0] > 200, "the scratch composite lands through the blit");
        target.Destroy();
    }

    [Fact]
    public void A_missing_preset_degrades_by_name()
    {
        SkipWithoutLibrashader();
        var options = new RashaderPresetOptions { Runtime = RashaderRuntime.Vulkan };
        var preset = RashaderPreset.TryCreate("/nonexistent/preset.slangp", options, out var whyNot);
        Assert.Null(preset);
        Assert.NotNull(whyNot);
    }

    [Fact]
    public void A_preset_naming_a_missing_shader_degrades_by_name()
    {
        SkipWithoutLibrashader();
        var options = new RashaderPresetOptions { Runtime = RashaderRuntime.Vulkan };
        var preset = RashaderPreset.TryCreate(FixturePath("broken.slangp"), options, out var whyNot);
        if (preset is not null)
        {
            CompositorTestHost.SkipUnlessRunnable("vulkan");
            using var host = new CompositorTestHost(renderer: "vulkan");
            preset.Dispose();
            var filter = RashaderFilter.TryCreate(
                ((VulkanRenderer)host.Renderer).Device,
                FixturePath("broken.slangp"),
                new RashaderFilterSettings { DisableCache = true },
                out whyNot);
            Assert.Null(filter);
        }

        Assert.NotNull(whyNot);
    }

    [Fact]
    public void The_preset_reports_its_parameters()
    {
        SkipWithoutLibrashader();
        var options = new RashaderPresetOptions { Runtime = RashaderRuntime.Vulkan };
        using var preset = RashaderPreset.TryCreate(FixturePath("invert.slangp"), options, out var whyNot);
        Assert.SkipWhen(preset is null, $"fixture preset rejected: {whyNot}");
        var parameter = Assert.Single(preset.Parameters, static p => p.Name == "InvertAmount");
        Assert.Equal(1f, parameter.Initial);
        Assert.Equal(0f, parameter.Minimum);
        Assert.Equal(1f, parameter.Maximum);
    }

    [Fact]
    public void The_filter_inverts_the_composited_screen()
    {
        CompositorTestHost.SkipUnlessRunnable("vulkan");
        SkipWithoutLibrashader();
        using var host = new CompositorTestHost(renderer: "vulkan");
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var state = new OutputState();

        _ = new SceneRect(host.Scene.Root, 160, 120, new RenderColor(1f, 0f, 0f, 1f));
        using var filter = RashaderFilter.TryCreate(
            ((VulkanRenderer)host.Renderer).Device,
            FixturePath("invert.slangp"),
            new RashaderFilterSettings { DisableCache = true },
            out var whyNot);
        Assert.SkipWhen(filter is null, $"fixture preset rejected: {whyNot}");
        sceneOutput.SetFrameFilter(filter);

        var target = new MemoryBuffer(160, 120, DrmFormat.Xrgb8888);
        Assert.True(sceneOutput.Commit(host.Renderer, target, 0, state, new SceneCommitOptions { AllowDirectScanout = false }));
        var rgba = ReadRgba(target);
        var center = ((60 * 160) + 80) * 4;
        Assert.True(rgba[center + 0] < 60, "red inverts away");
        Assert.True(rgba[center + 1] > 200, "green inverts on");
        Assert.True(rgba[center + 2] > 200, "blue inverts on");

        Assert.True(filter.TrySetParameter("InvertAmount", 0f));
        Assert.False(filter.TrySetParameter("NoSuchParameter", 1f));
        sceneOutput.SetFrameFilter(filter);
        Assert.True(sceneOutput.Commit(host.Renderer, target, 0, state, new SceneCommitOptions { AllowDirectScanout = false }));
        rgba = ReadRgba(target);
        Assert.True(rgba[center + 0] > 200, "the parameter turns the invert off");
        target.Destroy();
    }

    [Fact]
    public void The_gl_filter_inverts_the_composited_screen_without_flipping_it()
    {
        CompositorTestHost.SkipUnlessRunnable("gl");
        SkipWithoutLibrashader();
        using var host = new CompositorTestHost(renderer: "gl");
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var state = new OutputState();

        _ = new SceneRect(host.Scene.Root, 160, 120, new RenderColor(0f, 0f, 1f, 1f));
        var corner = new SceneRect(host.Scene.Root, 40, 30, new RenderColor(1f, 0f, 0f, 1f));
        corner.SetPosition(0, 0);
        using var filter = RashaderGlFilter.TryCreate(
            ((GlRenderer)host.Renderer).Device,
            FixturePath("invert.slangp"),
            new RashaderFilterSettings { DisableCache = true },
            out var whyNot);
        Assert.SkipWhen(filter is null, $"fixture preset rejected: {whyNot}");
        sceneOutput.SetFrameFilter(filter);

        var target = new MemoryBuffer(160, 120, DrmFormat.Xrgb8888);
        Assert.True(sceneOutput.Commit(host.Renderer, target, 0, state, new SceneCommitOptions { AllowDirectScanout = false }));
        var rgba = ReadRgba(target);
        int Channel(int x, int y, int c) => rgba[(((y * 160) + x) * 4) + c];
        Assert.True(
            Channel(10, 10, 0) < 60,
            $"the corner's red inverts away (10,10 = {Channel(10, 10, 0)},{Channel(10, 10, 1)},{Channel(10, 10, 2)}; 80,100 = {Channel(80, 100, 0)},{Channel(80, 100, 1)},{Channel(80, 100, 2)}; 10,110 = {Channel(10, 110, 0)},{Channel(10, 110, 1)},{Channel(10, 110, 2)})");
        Assert.True(Channel(10, 10, 1) > 200 && Channel(10, 10, 2) > 200, "the corner inverts to cyan in place");
        Assert.True(Channel(80, 100, 0) > 200 && Channel(80, 100, 1) > 200, "the background inverts to yellow");
        Assert.True(Channel(80, 100, 2) < 60, "the background's blue inverts away");

        Assert.True(filter.TrySetParameter("InvertAmount", 0f));
        Assert.False(filter.TrySetParameter("NoSuchParameter", 1f));
        sceneOutput.SetFrameFilter(filter);
        Assert.True(sceneOutput.Commit(host.Renderer, target, 0, state, new SceneCommitOptions { AllowDirectScanout = false }));
        rgba = ReadRgba(target);
        Assert.True(Channel(10, 10, 0) > 200, "the parameter turns the invert off");
        target.Destroy();
    }

    [Fact]
    public void A_vulkan_stack_runs_its_chains_in_order_with_independent_parameters()
    {
        CompositorTestHost.SkipUnlessRunnable("vulkan");
        SkipWithoutLibrashader();
        using var host = new CompositorTestHost(renderer: "vulkan");
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var state = new OutputState();

        _ = new SceneRect(host.Scene.Root, 160, 120, new RenderColor(1f, 0f, 0f, 1f));
        var settings = new RashaderFilterSettings { DisableCache = true };
        var device = ((VulkanRenderer)host.Renderer).Device;
        var first = RashaderFilter.TryCreate(device, FixturePath("invert.slangp"), settings, out var whyNot);
        Assert.SkipWhen(first is null, $"fixture preset rejected: {whyNot}");
        var second = RashaderFilter.TryCreate(device, FixturePath("invert.slangp"), settings, out whyNot);
        Assert.NotNull(second);
        using var stack = new RashaderFilterStack([first, second]);
        sceneOutput.SetFrameFilter(stack);

        var target = new MemoryBuffer(160, 120, DrmFormat.Xrgb8888);
        Assert.True(sceneOutput.Commit(host.Renderer, target, 0, state, new SceneCommitOptions { AllowDirectScanout = false }));
        var rgba = ReadRgba(target);
        var center = ((60 * 160) + 80) * 4;
        Assert.True(rgba[center + 0] > 200, "two inversions cancel");
        Assert.True(rgba[center + 1] < 60, "green stays off through both chains");

        Assert.True(second.TrySetParameter("InvertAmount", 0f));
        sceneOutput.SetFrameFilter(stack);
        Assert.True(sceneOutput.Commit(host.Renderer, target, 0, state, new SceneCommitOptions { AllowDirectScanout = false }));
        rgba = ReadRgba(target);
        Assert.True(rgba[center + 0] < 60, "only the first chain inverts now");
        Assert.True(rgba[center + 1] > 200 && rgba[center + 2] > 200, "the single inversion shows through");
        target.Destroy();
    }

    [Fact]
    public void A_gl_stack_runs_its_chains_in_order_with_independent_parameters()
    {
        CompositorTestHost.SkipUnlessRunnable("gl");
        SkipWithoutLibrashader();
        using var host = new CompositorTestHost(renderer: "gl");
        using var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var state = new OutputState();

        _ = new SceneRect(host.Scene.Root, 160, 120, new RenderColor(1f, 0f, 0f, 1f));
        var settings = new RashaderFilterSettings { DisableCache = true };
        var device = ((GlRenderer)host.Renderer).Device;
        var first = RashaderGlFilter.TryCreate(device, FixturePath("invert.slangp"), settings, out var whyNot);
        Assert.SkipWhen(first is null, $"fixture preset rejected: {whyNot}");
        var second = RashaderGlFilter.TryCreate(device, FixturePath("invert.slangp"), settings, out whyNot);
        Assert.NotNull(second);
        using var stack = new RashaderGlFilterStack([first, second]);
        sceneOutput.SetFrameFilter(stack);

        var target = new MemoryBuffer(160, 120, DrmFormat.Xrgb8888);
        Assert.True(sceneOutput.Commit(host.Renderer, target, 0, state, new SceneCommitOptions { AllowDirectScanout = false }));
        var rgba = ReadRgba(target);
        var center = ((60 * 160) + 80) * 4;
        Assert.True(rgba[center + 0] > 200, "two inversions cancel");
        Assert.True(rgba[center + 1] < 60, "green stays off through both chains");

        Assert.True(second.TrySetParameter("InvertAmount", 0f));
        sceneOutput.SetFrameFilter(stack);
        Assert.True(sceneOutput.Commit(host.Renderer, target, 0, state, new SceneCommitOptions { AllowDirectScanout = false }));
        rgba = ReadRgba(target);
        Assert.True(rgba[center + 0] < 60, "only the first chain inverts now");
        Assert.True(rgba[center + 1] > 200 && rgba[center + 2] > 200, "the single inversion shows through");
        target.Destroy();
    }

    [Fact]
    public void Gl_chains_create_and_free_without_leaking()
    {
        CompositorTestHost.SkipUnlessRunnable("gl");
        SkipWithoutLibrashader();
        using var host = new CompositorTestHost(renderer: "gl");
        var device = ((GlRenderer)host.Renderer).Device;
        string? whyNot = null;
        for (var i = 0; i < 10; i++)
        {
            using var filter = RashaderGlFilter.TryCreate(
                device, FixturePath("invert.slangp"), new RashaderFilterSettings { DisableCache = true }, out whyNot);
            Assert.SkipWhen(filter is null && i == 0, $"fixture preset rejected: {whyNot}");
            Assert.NotNull(filter);
        }
    }

    [Fact]
    public void Chains_create_and_free_without_leaking()
    {
        CompositorTestHost.SkipUnlessRunnable("vulkan");
        SkipWithoutLibrashader();
        using var host = new CompositorTestHost(renderer: "vulkan");
        var device = ((VulkanRenderer)host.Renderer).Device;
        string? whyNot = null;
        for (var i = 0; i < 20; i++)
        {
            using var filter = RashaderFilter.TryCreate(
                device, FixturePath("invert.slangp"), new RashaderFilterSettings { DisableCache = true }, out whyNot);
            Assert.SkipWhen(filter is null && i == 0, $"fixture preset rejected: {whyNot}");
            Assert.NotNull(filter);
        }
    }
}
