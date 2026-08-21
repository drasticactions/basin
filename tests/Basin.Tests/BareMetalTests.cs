using Basin.Backend.Drm;
using Xunit;

namespace Basin.Tests;

public sealed class CrtcAssignmentTests
{
    [Fact]
    public void Assigns_all_when_unconstrained()
    {
        var result = CrtcAssignment.Solve(
        [
            new CrtcCandidate(0b111, -1),
            new CrtcCandidate(0b111, -1),
            new CrtcCandidate(0b111, -1),
        ], 3);
        Assert.Equal(3, result.Distinct().Count());
        Assert.DoesNotContain(-1, result);
    }

    [Fact]
    public void Respects_possible_crtcs_masks()
    {
        var result = CrtcAssignment.Solve(
        [
            new CrtcCandidate(0b001, -1),
            new CrtcCandidate(0b001, -1),
        ], 3);
        Assert.Single(result, r => r == 0);
        Assert.Single(result, r => r == -1);
    }

    [Fact]
    public void Constrained_connector_wins_the_contested_crtc()
    {
        var result = CrtcAssignment.Solve(
        [
            new CrtcCandidate(0b010, -1),
            new CrtcCandidate(0b011, -1),
        ], 2);
        Assert.Equal(1, result[0]);
        Assert.Equal(0, result[1]);
    }

    [Fact]
    public void Hotplug_does_not_disturb_lit_outputs()
    {
        var result = CrtcAssignment.Solve(
        [
            new CrtcCandidate(0b011, 1),
            new CrtcCandidate(0b011, -1),
        ], 2);
        Assert.Equal(1, result[0]);
        Assert.Equal(0, result[1]);
    }

    [Fact]
    public void More_connectors_than_crtcs_lights_as_many_as_possible()
    {
        var result = CrtcAssignment.Solve(
        [
            new CrtcCandidate(0b11, -1),
            new CrtcCandidate(0b11, -1),
            new CrtcCandidate(0b11, -1),
        ], 2);
        Assert.Equal(2, result.Count(r => r >= 0));
        Assert.Equal(1, result.Count(r => r == -1));
    }
}

public sealed class EdidTests
{
    [Fact]
    public void Parses_a_real_monitor_edid()
    {
        var edid = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mateview.edid"));
        var info = EdidInfo.Parse(edid);
        Assert.Equal("HWV", info.Make);
        Assert.Equal("MateView", info.Model);
        Assert.NotEmpty(info.Serial);

        Assert.NotNull(info.Chromaticities);
        var c = info.Chromaticities!.Value;
        Assert.Equal(0.680, c.Rx, 2);
        Assert.Equal(0.320, c.Ry, 2);
        Assert.Equal(0.313, c.Wx, 2);
        Assert.True(info.SupportsPq);
        Assert.False(info.SupportsHlg);
        Assert.True(info.SupportsBt2020);
        Assert.Equal(496.7, info.MaxLuminance, 1);
        Assert.InRange(info.MinLuminance, 0.00001, 0.001);
    }

    [Fact]
    public void Garbage_is_not_fatal()
    {
        Assert.Equal("unknown", EdidInfo.Parse([1, 2, 3]).Make);
        var zeros = new byte[128];
        _ = EdidInfo.Parse(zeros);
    }
}

public sealed class XcursorTests
{
    [Fact]
    public void Parses_a_synthetic_two_frame_cursor()
    {
        var file = BuildXcursor(
            (Size: 24u, Width: 24u, Height: 24u, HotX: 3u, HotY: 4u, Delay: 50u),
            (Size: 24u, Width: 24u, Height: 24u, HotX: 3u, HotY: 4u, Delay: 60u),
            (Size: 48u, Width: 48u, Height: 48u, HotX: 6u, HotY: 8u, Delay: 0u));

        var small = XcursorTheme.Parse(file, 24);
        Assert.NotNull(small);
        Assert.Equal(2, small!.Frames.Count);
        Assert.Equal((24, 24, 3, 4, 50), (small.Frames[0].Width, small.Frames[0].Height, small.Frames[0].HotspotX, small.Frames[0].HotspotY, small.Frames[0].DelayMs));
        Assert.Equal(24 * 24 * 4, small.Frames[0].Pixels.Length);

        var large = XcursorTheme.Parse(file, 48);
        Assert.NotNull(large);
        Assert.Single(large!.Frames);
        Assert.Equal(48, large.Frames[0].Width);

        var mid = XcursorTheme.Parse(file, 30);
        Assert.Equal(24, mid!.Frames[0].Width);
    }

    [Fact]
    public void Rejects_garbage_without_throwing()
    {
        Assert.Null(XcursorTheme.Parse([1, 2, 3, 4], 24));
        Assert.Null(XcursorTheme.Parse(new byte[64], 24));
    }

    [Fact]
    public void Loads_the_system_theme_when_present()
    {
        Assert.SkipUnless(Directory.Exists("/usr/share/icons/Adwaita/cursors"), "no Adwaita theme installed");
        var theme = XcursorTheme.Load("Adwaita", 24, ["/usr/share/icons"]);
        Assert.NotNull(theme);
        var arrow = theme!.Get("left_ptr") ?? theme.Get("default");
        Assert.NotNull(arrow);
        Assert.True(arrow!.Frames[0].Width > 0);
        Assert.True(arrow.Frames[0].Pixels.Length == arrow.Frames[0].Width * arrow.Frames[0].Height * 4);
    }

    private static byte[] BuildXcursor(params (uint Size, uint Width, uint Height, uint HotX, uint HotY, uint Delay)[] images)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0x72756358u);
        writer.Write(16u);
        writer.Write(1u);
        writer.Write((uint)images.Length);

        var chunkOffset = 16 + images.Length * 12;
        foreach (var image in images)
        {
            writer.Write(0xFFFD0002u);
            writer.Write(image.Size);
            writer.Write((uint)chunkOffset);
            chunkOffset += 36 + (int)(image.Width * image.Height * 4);
        }

        foreach (var image in images)
        {
            writer.Write(36u);
            writer.Write(0xFFFD0002u);
            writer.Write(image.Size);
            writer.Write(1u);
            writer.Write(image.Width);
            writer.Write(image.Height);
            writer.Write(image.HotX);
            writer.Write(image.HotY);
            writer.Write(image.Delay);
            writer.Write(new byte[image.Width * image.Height * 4]);
        }

        return stream.ToArray();
    }
}

public sealed class LayoutPointerTests
{
    private sealed class FakeOutput(string name) : OutputBase(name)
    {
        protected override bool TestCommitCore(OutputState state) => true;

        protected override bool CommitCore(OutputState state) => true;
    }

    private static (OutputLayout Layout, LayoutPointer Pointer, IOutput Left, IOutput Right) TwoOutputs()
    {
        var layout = new OutputLayout();
        var left = new FakeOutput("L");
        var right = new FakeOutput("R");
        using var state = new OutputState();
        left.Commit(state.SetEnabled(true).SetMode(new OutputMode(100, 100, 60000)));
        state.Clear();
        right.Commit(state.SetEnabled(true).SetMode(new OutputMode(200, 100, 60000)));
        layout.Add(left, 0, 0);
        layout.Add(right, 100, 0);
        return (layout, new LayoutPointer(layout), left, right);
    }

    [Fact]
    public void Motion_crosses_output_boundaries()
    {
        var (_, pointer, _, _) = TwoOutputs();
        pointer.Warp(90, 50);
        pointer.Motion(30, 0);
        Assert.Equal((120d, 50d), (pointer.X, pointer.Y));
    }

    [Fact]
    public void Motion_is_constrained_to_the_layout()
    {
        var (_, pointer, _, _) = TwoOutputs();
        pointer.Warp(50, 50);
        pointer.Motion(-500, -500);
        Assert.Equal((0d, 0d), (pointer.X, pointer.Y));

        pointer.Motion(5000, 20);
        Assert.Equal(299d, pointer.X);
    }

    [Fact]
    public void Diagonal_escape_through_the_corner_gap_is_clamped()
    {
        var (_, pointer, _, _) = TwoOutputs();
        pointer.Warp(250, 90);
        pointer.Motion(0, 50);
        Assert.Equal((250d, 99d), (pointer.X, pointer.Y));
    }

    [Fact]
    public void Absolute_motion_maps_into_one_output()
    {
        var (_, pointer, _, right) = TwoOutputs();
        pointer.MotionAbsolute(right, 0.5, 0.5);
        Assert.Equal((200d, 50d), (pointer.X, pointer.Y));
    }

    [Fact]
    public void Absolute_motion_without_binding_spans_the_layout()
    {
        var (_, pointer, _, _) = TwoOutputs();
        pointer.MotionAbsolute(null, 1.0, 0.5);
        Assert.Equal(299d, pointer.X);
    }

    private static void Rescale(IOutput output, double scale)
    {
        using var state = new OutputState();
        output.Commit(state.SetScale(scale));
    }

    [Fact]
    public void Doubling_the_scale_leaves_the_pointer_where_it_was_on_screen()
    {
        var layout = new OutputLayout();
        var output = new FakeOutput("eDP-1");
        using (var state = new OutputState())
        {
            output.Commit(state.SetEnabled(true).SetMode(new OutputMode(1920, 1200, 60000)));
        }

        layout.Add(output, 0, 0);
        var pointer = new LayoutPointer(layout);
        pointer.Warp(960, 600);

        Rescale(output, 2);
        pointer.Reposition();

        Assert.Equal((480d, 300d), (pointer.X, pointer.Y));
    }

    [Fact]
    public void Halving_the_scale_leaves_the_pointer_where_it_was_on_screen()
    {
        var layout = new OutputLayout();
        var output = new FakeOutput("eDP-1");
        using (var state = new OutputState())
        {
            output.Commit(state.SetEnabled(true).SetMode(new OutputMode(1920, 1200, 60000)).SetScale(2));
        }

        layout.Add(output, 0, 0);
        var pointer = new LayoutPointer(layout);
        pointer.Warp(480, 300);

        Rescale(output, 1);
        pointer.Reposition();

        Assert.Equal((960d, 600d), (pointer.X, pointer.Y));
    }

    [Fact]
    public void Repositioning_holds_the_pointer_on_the_output_it_was_on()
    {
        var (layout, pointer, _, right) = TwoOutputs();
        pointer.Warp(200, 50);
        Assert.Same(right, layout.OutputAt(pointer.X, pointer.Y));

        Rescale(right, 2);
        pointer.Reposition();

        Assert.Same(right, layout.OutputAt(pointer.X, pointer.Y));
        Assert.Equal((150d, 25d), (pointer.X, pointer.Y));
    }

    [Fact]
    public void A_pointer_whose_output_is_gone_falls_back_to_the_nearest_point()
    {
        var (layout, pointer, left, right) = TwoOutputs();
        pointer.Warp(200, 50);

        layout.Remove(right);
        pointer.Reposition();

        Assert.Same(left, layout.OutputAt(pointer.X, pointer.Y));
        Assert.Equal(99d, pointer.X);
    }
}
