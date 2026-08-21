using Basin.Backend.Headless;
using Basin.Capabilities;
using Basin.Desktop;
using Pixman;
using Xunit;

namespace Basin.Tests;

public sealed class CursorControllerTests
{
    private static HeadlessOutput Output(HeadlessBackend backend) =>
        backend.CreateOutput(new OutputMode(160, 120, 60000));

    [Fact]
    public void The_cursor_follows_a_scale_committed_after_it_was_loaded()
    {
        using var host = new CompositorTestHost();
        using var backend = new HeadlessBackend(host.Loop);
        var output = Output(backend);
        var layout = new OutputLayout();
        layout.Add(output, 0, 0);

        using var cursor = new CursorController(layout);
        cursor.AddOutput(output, null);
        cursor.Load(new ShmAllocator(), 64, 64);
        Assert.SkipWhen(cursor.Images?.HasTheme != true, "no xcursor theme installed");

        Assert.Equal(24, cursor.Images!.Size);

        using var state = new OutputState();
        Assert.True(output.Commit(state.SetScale(2)));

        Assert.Equal(48, cursor.Images.Size);
    }

    [Fact]
    public void A_frame_commit_does_not_touch_the_cursor()
    {
        using var host = new CompositorTestHost();
        using var backend = new HeadlessBackend(host.Loop);
        var output = Output(backend);
        var layout = new OutputLayout();
        layout.Add(output, 0, 0);

        using var cursor = new CursorController(layout);
        cursor.AddOutput(output, null);
        cursor.Load(new ShmAllocator(), 64, 64);
        Assert.SkipWhen(cursor.Images?.HasTheme != true, "no xcursor theme installed");

        var variants = cursor.Images!.VariantCount;
        using var damage = new PixmanRegion32();
        damage.UnionRect(damage, 0, 0, 160, 120);
        using var state = new OutputState();
        Assert.True(output.Commit(state.SetDamage(damage)));

        Assert.Equal(variants, cursor.Images.VariantCount);
    }

    [Fact]
    public void The_shape_scale_follows_the_densest_output()
    {
        using var host = new CompositorTestHost();
        using var backend = new HeadlessBackend(host.Loop);
        var output = Output(backend);
        var layout = new OutputLayout();
        layout.Add(output, 0, 0);

        using var shapes = new CursorShapeManager(host.Display, null);
        using var cursor = new CursorController(layout) { Shapes = shapes };
        cursor.AddOutput(output, null);

        Assert.Equal(1, shapes.Scale);

        using var state = new OutputState();
        Assert.True(output.Commit(state.SetScale(2)));

        Assert.Equal(2, shapes.Scale);
    }

    [Fact]
    public void Each_output_gets_the_cursor_its_own_scale_asks_for()
    {
        using var host = new CompositorTestHost();
        using var backend = new HeadlessBackend(host.Loop);
        var dense = Output(backend);
        var plain = Output(backend);
        var layout = new OutputLayout();
        layout.Add(dense, 0, 0);
        layout.Add(plain, 200, 0);

        using var denseState = new OutputState();
        Assert.True(dense.Commit(denseState.SetScale(2)));

        using var cursor = new CursorController(layout);
        cursor.AddOutput(dense, null);
        cursor.AddOutput(plain, null);
        cursor.Load(new ShmAllocator(), 64, 64);
        Assert.SkipWhen(cursor.Images?.HasTheme != true, "no xcursor theme installed");

        cursor.MoveTo(10, 10);
        Assert.Equal(48, cursor.Images!.Size);

        cursor.MoveTo(210, 10);
        Assert.Equal(24, cursor.Images.Size);
    }

    [Fact]
    public void A_description_change_re_encodes_the_cursor()
    {
        using var host = new CompositorTestHost();
        using var backend = new HeadlessBackend(host.Loop);
        var output = Output(backend);
        var layout = new OutputLayout();
        layout.Add(output, 0, 0);

        using var cursor = new CursorController(layout);
        cursor.AddOutput(output, null);
        cursor.Load(new ShmAllocator(), 64, 64);
        Assert.SkipWhen(cursor.Images?.HasTheme != true, "no xcursor theme installed");

        cursor.MoveTo(10, 10);
        var before = cursor.Images!.VariantCount;

        cursor.Describe(output, new ImageDescription { PrimariesNamed = ColorPrimaries.Bt2020 });
        cursor.MoveTo(11, 11);

        Assert.Equal(before + 1, cursor.Images.VariantCount);
    }

    [Fact]
    public void An_applied_configuration_is_announced_once_for_the_whole_batch()
    {
        using var host = new CompositorTestHost();
        using var backend = new HeadlessBackend(host.Loop);
        var first = Output(backend);
        var second = Output(backend);
        var layout = new OutputLayout();
        layout.Add(first, 0, 0);
        layout.Add(second, 200, 0);

        var configuration = new Basin.Capabilities.Defaults.LayoutOutputConfiguration(layout);
        var announced = 0;
        IReadOnlyList<OutputConfigurationEntry>? seen = null;
        configuration.Applied += entries =>
        {
            announced++;
            seen = entries;
        };

        OutputConfigurationEntry[] batch =
        [
            new() { Output = first, Enabled = true, Scale = 2 },
            new() { Output = second, Enabled = true, Scale = 1 },
        ];

        Assert.True(configuration.Apply(batch));
        Assert.Equal(1, announced);
        Assert.Equal(2, seen!.Count);
        Assert.Equal(2, first.Scale);
    }

    [Fact]
    public void A_configuration_that_cannot_be_tested_is_never_announced()
    {
        using var host = new CompositorTestHost();
        using var backend = new HeadlessBackend(host.Loop);
        var output = Output(backend);
        var layout = new OutputLayout();
        layout.Add(output, 0, 0);

        var configuration = new Basin.Capabilities.Defaults.LayoutOutputConfiguration(layout);
        var announced = 0;
        configuration.Applied += _ => announced++;

        OutputConfigurationEntry[] batch =
        [
            new() { Output = output, Enabled = true, Scale = 2, AdaptiveSync = true },
        ];

        Assert.False(configuration.Apply(batch));
        Assert.Equal(0, announced);
        Assert.Equal(1, output.Scale);
    }

    [Fact]
    public void A_refresh_in_parent_mode_never_lands_in_the_scene()
    {
        using var host = new CompositorTestHost();
        using var backend = new HeadlessBackend(host.Loop);
        var output = Output(backend);
        var layout = new OutputLayout();
        layout.Add(output, 0, 0);

        var parent = new RecordingParentCursor();
        using var cursor = new CursorController(layout);
        cursor.AddOutput(output, null);
        cursor.AttachParent(parent);
        cursor.Load(new ShmAllocator(), 64, 64);
        Assert.SkipWhen(cursor.Images?.HasTheme != true, "no xcursor theme installed");

        var shown = parent.Shown;
        using var state = new OutputState();
        Assert.True(output.Commit(state.SetScale(2)));

        Assert.False(cursor.IsSoftwareOn(output));
        Assert.True(parent.Shown > shown);
        Assert.Equal("parent", cursor.DrawnBy);
    }

    [Fact]
    public void Hiding_takes_the_cursor_off_its_output_and_revealing_puts_it_back()
    {
        using var host = new CompositorTestHost();
        using var backend = new HeadlessBackend(host.Loop);
        var output = Output(backend);
        var layout = new OutputLayout();
        layout.Add(output, 0, 0);

        using var cursor = new CursorController(layout);
        cursor.AddOutput(output, null);
        cursor.Load(new ShmAllocator(), 64, 64);
        Assert.SkipWhen(cursor.Images?.HasTheme != true, "no xcursor theme installed");

        cursor.MoveTo(20, 20);
        cursor.ShowNamed("left_ptr");
        Assert.False(cursor.IsHidden);
        Assert.NotNull(cursor.CursorOutput);

        cursor.Hide();
        Assert.True(cursor.IsHidden);
        Assert.Null(cursor.CursorOutput);

        cursor.Reveal();
        Assert.False(cursor.IsHidden);
        Assert.NotNull(cursor.CursorOutput);
    }

    [Fact]
    public void A_hidden_cursor_keeps_the_image_it_was_asked_for()
    {
        using var host = new CompositorTestHost();
        using var backend = new HeadlessBackend(host.Loop);
        var output = Output(backend);
        var layout = new OutputLayout();
        layout.Add(output, 0, 0);

        using var cursor = new CursorController(layout);
        cursor.AddOutput(output, null);
        cursor.Load(new ShmAllocator(), 64, 64);
        Assert.SkipWhen(cursor.Images?.HasTheme != true, "no xcursor theme installed");

        cursor.MoveTo(20, 20);
        cursor.ShowNamed("left_ptr");
        cursor.Hide();

        cursor.MoveTo(30, 30);
        Assert.Null(cursor.CursorOutput);
        Assert.Equal("left_ptr", cursor.Showing);

        cursor.Reveal();
        Assert.Equal("left_ptr", cursor.Showing);
        Assert.NotNull(cursor.CursorOutput);
    }

    [Fact]
    public void Hiding_a_nested_cursor_asks_the_parent_to_hide_it()
    {
        using var host = new CompositorTestHost();
        using var backend = new HeadlessBackend(host.Loop);
        var output = Output(backend);
        var layout = new OutputLayout();
        layout.Add(output, 0, 0);

        using var cursor = new CursorController(layout);
        var parent = new RecordingParentCursor();
        cursor.AddOutput(output, null);
        cursor.UseParentCursor();
        cursor.AttachParent(parent);
        cursor.Load(new ShmAllocator(), 64, 64);
        Assert.SkipWhen(cursor.Images?.HasTheme != true, "no xcursor theme installed");

        cursor.MoveTo(20, 20);
        cursor.ShowNamed("left_ptr");
        var shown = parent.Shown;

        cursor.Hide();
        Assert.Equal(1, parent.Hidden);

        cursor.Reveal();
        Assert.True(parent.Shown > shown);
    }

    private sealed class RecordingParentCursor : IParentCursor
    {
        public int Shown { get; private set; }

        public int Hidden { get; private set; }

        public bool SetCursor(IBuffer image, int hotspotX, int hotspotY, double scale = 1.0)
        {
            Shown++;
            return true;
        }

        public void HideCursor() => Hidden++;
    }

    [Fact]
    public void A_removed_output_stops_driving_the_cursor()
    {
        using var host = new CompositorTestHost();
        using var backend = new HeadlessBackend(host.Loop);
        var output = Output(backend);
        var layout = new OutputLayout();
        layout.Add(output, 0, 0);

        using var cursor = new CursorController(layout);
        cursor.AddOutput(output, null);
        cursor.Load(new ShmAllocator(), 64, 64);
        Assert.SkipWhen(cursor.Images?.HasTheme != true, "no xcursor theme installed");

        cursor.RemoveOutput(output);
        var variants = cursor.Images!.VariantCount;

        using var state = new OutputState();
        Assert.True(output.Commit(state.SetScale(3)));

        Assert.Equal(variants, cursor.Images.VariantCount);
    }
}
