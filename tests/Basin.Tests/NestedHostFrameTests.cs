using Basin.Backend.Wayland;
using Basin.Protocol;
using Xunit;

namespace Basin.Tests;

public class NestedHostFrameTests
{
    private static readonly HostFrameInsets Chrome = new(Top: 24, Right: 4, Bottom: 4, Left: 4);

    private static void Map(NestedBackendTestHost host, WaylandOutput output)
    {
        var content = new MemoryBuffer(output.CurrentMode.Width, output.CurrentMode.Height, DrmFormat.Xrgb8888);
        using (var state = new OutputState())
        {
            Assert.True(output.Commit(state.SetBuffer(content)));
        }

        host.Pump();
    }

    [Fact]
    public void Host_frame_appears_when_the_parent_will_not_decorate()
    {
        using var host = new NestedBackendTestHost(NestedParentOptions.Undecorating);
        var output = host.CreateOutput();

        Assert.False(output.Decorated);
        Assert.NotNull(output.HostFrame);

        Assert.True(output.HostFrame!.Insets.IsEmpty);
        Assert.Equal(800, output.CurrentMode.Width);
        Assert.Equal(600, output.CurrentMode.Height);
    }

    [Fact]
    public void Host_frame_stays_away_when_the_parent_decorates()
    {
        using var host = new NestedBackendTestHost(NestedParentOptions.Decorating);
        var output = host.CreateOutput();

        Assert.True(output.Decorated);
        Assert.Null(output.HostFrame);
    }

    [Fact]
    public void Host_frame_needs_a_viewporter_to_crop_its_bands()
    {
        using var host = new NestedBackendTestHost(NestedParentOptions.Undecorating with { Viewporter = false });
        var output = host.CreateOutput();

        Assert.False(output.Decorated);
        Assert.Null(output.HostFrame);
    }

    [Fact]
    public void Host_frame_needs_a_subcompositor_to_hang_bands_on()
    {
        using var host = new NestedBackendTestHost(NestedParentOptions.Undecorating with { Subcompositor = false });
        var output = host.CreateOutput();

        Assert.Null(output.HostFrame);
    }

    [Fact]
    public void Insets_come_out_of_the_output_mode()
    {
        using var host = new NestedBackendTestHost(NestedParentOptions.Undecorating);
        var output = host.CreateOutput();

        Assert.True(output.HostFrame!.SetInsets(Chrome));
        host.Pump();

        Assert.Equal(800 - Chrome.Left - Chrome.Right, output.CurrentMode.Width);
        Assert.Equal(600 - Chrome.Top - Chrome.Bottom, output.CurrentMode.Height);
        Assert.Equal(800, output.HostFrame.OuterWidth);
        Assert.Equal(600, output.HostFrame.OuterHeight);
    }

    [Fact]
    public void Clearing_the_insets_gives_the_output_its_size_back()
    {
        using var host = new NestedBackendTestHost(NestedParentOptions.Undecorating);
        var output = host.CreateOutput();

        Assert.True(output.HostFrame!.SetInsets(Chrome));
        host.Pump();
        Assert.True(output.HostFrame.SetInsets(default));
        host.Pump();

        Assert.Equal(800, output.CurrentMode.Width);
        Assert.Equal(600, output.CurrentMode.Height);
    }

    [Fact]
    public void Nothing_reaches_the_parent_before_the_first_frame()
    {
        using var host = new NestedBackendTestHost(NestedParentOptions.Undecorating);
        var output = host.CreateOutput();
        var commits = 0;
        host.Parent.Invoke(() => host.Parent.Toplevels[0].Xdg.Surface.Committed += () => commits++);

        output.HostFrame!.SetInsets(Chrome);
        host.Pump();

        var seen = host.Parent.Invoke(() => (
            Commits: commits,
            host.Parent.Toplevels[0].Xdg.Surface.IsMapped,
            host.Parent.Toplevels[0].Xdg.WindowGeometry));

        Assert.Equal(0, seen.Commits);
        Assert.False(seen.IsMapped);
        Assert.True(seen.WindowGeometry.IsEmpty);
    }

    [Fact]
    public void A_frame_with_no_chrome_on_it_publishes_no_window_geometry()
    {
        using var host = new NestedBackendTestHost(NestedParentOptions.Undecorating);
        var output = host.CreateOutput();
        output.HostFrame!.SetInsets(Chrome);
        Map(host, output);

        var geometry = host.Parent.Invoke(() => host.Parent.Toplevels[0].Xdg.WindowGeometry);

        Assert.True(geometry.IsEmpty);
    }

    [Fact]
    public void Window_geometry_covers_the_chrome_as_well_as_the_output()
    {
        using var host = new NestedBackendTestHost(NestedParentOptions.Undecorating);
        var output = host.CreateOutput();
        var frame = output.HostFrame!;
        frame.SetInsets(Chrome);
        Map(host, output);
        Assert.True(frame.Attach(new MemoryBuffer(frame.OuterWidth, frame.OuterHeight, DrmFormat.Argb8888)));
        host.Pump();

        var geometry = host.Parent.Invoke(() => host.Parent.Toplevels[0].Xdg.WindowGeometry);

        Assert.Equal(-Chrome.Left, geometry.X);
        Assert.Equal(-Chrome.Top, geometry.Y);
        Assert.Equal(800, geometry.Width);
        Assert.Equal(600, geometry.Height);
    }

    [Fact]
    public void Insets_that_leave_no_room_are_clamped_rather_than_rejected()
    {
        using var host = new NestedBackendTestHost(NestedParentOptions.Undecorating with { Width = 140, Height = 140 });
        var output = host.CreateOutput();

        Assert.True(output.HostFrame!.SetInsets(new HostFrameInsets(120, 120, 120, 120)));
        host.Pump();

        Assert.True(output.CurrentMode.Width >= 128);
        Assert.True(output.CurrentMode.Height >= 128);
    }

    [Fact]
    public void Bands_reach_the_parent_cropped_out_of_one_buffer()
    {
        using var host = new NestedBackendTestHost(NestedParentOptions.Undecorating);
        var output = host.CreateOutput();
        var frame = output.HostFrame!;
        frame.SetInsets(Chrome);
        Map(host, output);

        var chrome = new MemoryBuffer(frame.OuterWidth, frame.OuterHeight, DrmFormat.Argb8888);
        Assert.True(frame.Attach(chrome));
        host.Pump();

        var bands = host.Parent.Invoke(() =>
        {
            var surface = host.Parent.Toplevels[0].Xdg.Surface;

            var all = surface.SubsurfacesBelow.Concat(surface.SubsurfacesAbove);
            return all
                .Select(s => (
                    s.X,
                    s.Y,
                    SourceX: s.Surface.Current.ViewportSourceX,
                    SourceY: s.Surface.Current.ViewportSourceY,
                    SourceWidth: s.Surface.Current.ViewportSourceWidth,
                    SourceHeight: s.Surface.Current.ViewportSourceHeight,
                    DestWidth: s.Surface.Current.ViewportDestinationWidth,
                    DestHeight: s.Surface.Current.ViewportDestinationHeight,
                    HasBuffer: s.Surface.Current.Buffer is not null))
                .OrderBy(b => b.Y)
                .ThenBy(b => b.X)
                .ToList();
        });

        Assert.Equal(4, bands.Count);
        Assert.All(bands, band => Assert.True(band.HasBuffer));

        var contentWidth = 800 - Chrome.Left - Chrome.Right;
        var contentHeight = 600 - Chrome.Top - Chrome.Bottom;

        var top = bands[0];
        Assert.Equal(-Chrome.Left, top.X);
        Assert.Equal(-Chrome.Top, top.Y);
        Assert.Equal(800, top.DestWidth);
        Assert.Equal(Chrome.Top, top.DestHeight);
        Assert.Equal(0, top.SourceX);
        Assert.Equal(0, top.SourceY);
        Assert.Equal(800, top.SourceWidth);
        Assert.Equal(Chrome.Top, top.SourceHeight);

        var left = bands[1];
        var right = bands[2];
        Assert.Equal(-Chrome.Left, left.X);
        Assert.Equal(contentWidth, right.X);
        Assert.Equal(0, left.Y);
        Assert.Equal(0, right.Y);
        Assert.Equal(contentHeight, left.DestHeight);
        Assert.Equal(contentHeight, right.DestHeight);

        Assert.Equal(0, left.SourceX);
        Assert.Equal(Chrome.Top, left.SourceY);
        Assert.Equal(Chrome.Left + contentWidth, right.SourceX);

        var bottom = bands[3];
        Assert.Equal(-Chrome.Left, bottom.X);
        Assert.Equal(contentHeight, bottom.Y);
        Assert.Equal(800, bottom.DestWidth);
        Assert.Equal(Chrome.Bottom, bottom.DestHeight);
        Assert.Equal(Chrome.Top + contentHeight, bottom.SourceY);
        chrome.Destroy();
    }

    [Fact]
    public void A_parent_resize_moves_the_bands_with_the_output()
    {
        using var host = new NestedBackendTestHost(NestedParentOptions.Undecorating);
        var output = host.CreateOutput();
        var frame = output.HostFrame!;
        frame.SetInsets(Chrome);
        Map(host, output);

        var chrome = new MemoryBuffer(frame.OuterWidth, frame.OuterHeight, DrmFormat.Argb8888);
        frame.Attach(chrome);
        host.Pump();

        host.Parent.Invoke(() =>
        {
            var toplevel = host.Parent.Toplevels[0];
            toplevel.SetSize(500, 400);
            toplevel.SendConfigure();
        });
        host.Pump();

        Assert.Equal(500 - Chrome.Left - Chrome.Right, output.CurrentMode.Width);
        Assert.Equal(400 - Chrome.Top - Chrome.Bottom, output.CurrentMode.Height);

        var rightBandX = host.Parent.Invoke(() =>
        {
            var surface = host.Parent.Toplevels[0].Xdg.Surface;
            var all = surface.SubsurfacesBelow.Concat(surface.SubsurfacesAbove);

            return all.Max(s => s.X);
        });

        Assert.Equal(500 - Chrome.Left - Chrome.Right, rightBandX);
        chrome.Destroy();
    }

    [Fact]
    public void Maximizing_the_parent_window_reaches_the_frame_state()
    {
        using var host = new NestedBackendTestHost(NestedParentOptions.Undecorating);
        var output = host.CreateOutput();
        var frame = output.HostFrame!;
        frame.SetInsets(Chrome);
        host.Pump();

        var changes = 0;
        frame.StateChanged += () => changes++;

        host.Parent.Invoke(() =>
        {
            var toplevel = host.Parent.Toplevels[0];
            toplevel.SetMaximized(true);
            toplevel.SendConfigure();
        });
        host.Pump();

        Assert.True(frame.Maximized);
        Assert.True(frame.Activated);
        Assert.True(changes > 0);
    }
}
