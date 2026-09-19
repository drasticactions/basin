using Basin.Render.Skia;
using Basin.Scene;
using Basin.Shell.Nested;
using Basin.Shell.Xdg;
using Xunit;

namespace Basin.Tests.Nested;

public sealed class NestedShellTests
{
    [Fact]
    public void A_mapped_client_is_framed_inside_the_work_area_and_the_panels_take_their_strips()
    {
        using var harness = new NestedShellHarness();
        var shell = harness.Shell;
        var toplevel = harness.MapToplevel(width: 200, height: 150, title: "notes", appId: "org.basin.notes", serverDecorated: true);

        var window = Assert.Single(shell.Windows);
        Assert.True(window.Decorated);
        Assert.NotNull(window.Frame);
        var insets = window.Insets;
        Assert.True(insets.Top > 0, "the frame has no title bar");
        Assert.True(insets.Left > 0 && insets.Right > 0 && insets.Bottom > 0, $"the frame has no borders: {insets}");
        Assert.Equal(new Box(0, 24, 800, 552), shell.WorkArea);
        Assert.True(shell.WorkArea.Contains(window.FrameBox), $"the frame {window.FrameBox} lies outside the work area");
        Assert.Equal(200, toplevel.ConfiguredWidth == 0 ? 200 : toplevel.ConfiguredWidth);
        Assert.Same(window, shell.Focused);

        using var renderer = new SkiaRenderer();
        var shot = new MemoryBuffer(800, 600, DrmFormat.Xrgb8888);
        try
        {
            Assert.True(harness.Host.Scene.Render(renderer, shot, new RenderColor(0f, 0f, 0f, 1f)));
            Assert.True(shot.BeginDataAccess(BufferDataAccess.Read, out var view));
            try
            {
                var client = window.ClientBox;
                var inside = Pixel(view, client.X + 10, client.Y + 10);
                Assert.Equal(0xFF3366AAu, inside & 0xFFFFFFFF);
                var title = Pixel(view, client.X + (client.Width / 2), client.Y - (insets.Top / 2));
                Assert.NotEqual(0xFF000000u, title);
            }
            finally
            {
                shot.EndDataAccess();
            }
        }
        finally
        {
            shot.Destroy();
        }

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [Fact]
    public void A_client_that_asks_for_nothing_keeps_its_own_decorations()
    {
        using var harness = new NestedShellHarness();
        var shell = harness.Shell;
        var toplevel = harness.MapToplevel(title: "csd");

        var window = Assert.Single(shell.Windows);
        Assert.False(window.Decorated);
        Assert.Null(window.Frame);
        Assert.Equal(window.ClientBox, window.FrameBox);

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [Fact]
    public void The_close_chord_reaches_the_focused_client_as_a_close()
    {
        using var harness = new NestedShellHarness();
        var shell = harness.Shell;
        var toplevel = harness.MapToplevel(title: "notes");

        Key(shell, 56, true);
        Key(shell, 62, true);
        Key(shell, 62, false);
        Key(shell, 56, false);
        harness.PumpUntil(() => toplevel.CloseReceived, "Alt+F4 did not close the client");

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [Fact]
    public void A_fractional_output_still_composes_the_frame_and_the_client()
    {
        using var harness = new NestedShellHarness(1334, 1000, 1.6666666666666667);
        var shell = harness.Shell;
        var toplevel = harness.MapToplevel(width: 200, height: 150, title: "notes", serverDecorated: true);
        var window = Assert.Single(shell.Windows);
        using var renderer = new SkiaRenderer();
        var shot = new MemoryBuffer(1334, 1000, DrmFormat.Xrgb8888);
        try
        {
            Assert.True(harness.Host.Scene.Render(renderer, shot, new RenderColor(0f, 0f, 0f, 1f), shell.Scale));
            Assert.True(shot.BeginDataAccess(BufferDataAccess.Read, out var view));
            try
            {
                var client = window.ClientBox;
                var inside = Pixel(view, (int)((client.X + 10) * shell.Scale), (int)((client.Y + 10) * shell.Scale));
                Assert.Equal(0xFF3366AAu, inside);
                var title = Pixel(view, (int)((client.X + (client.Width / 2)) * shell.Scale), (int)((client.Y - (window.Insets.Top / 2)) * shell.Scale));
                Assert.NotEqual(0xFF000000u, title);
            }
            finally
            {
                shot.EndDataAccess();
            }
        }
        finally
        {
            shot.Destroy();
        }

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [Fact]
    public void The_background_fills_the_output_in_the_configured_color_and_follows_a_resize()
    {
        using var harness = new NestedShellHarness(settings: new ShellSettings(Background: "#023c88"));
        var shell = harness.Shell;
        var rect = Assert.IsType<SceneRect>(Assert.Single(shell.Layers.Background.Children));
        Assert.Equal(new RenderColor(0x02 / 255f, 0x3c / 255f, 0x88 / 255f, 1f), rect.Color);
        Assert.Equal(new RenderColor(0x02 / 255f, 0x3c / 255f, 0x88 / 255f, 1f), shell.Background);
        Assert.Equal((shell.Output.Width, shell.Output.Height), (rect.Width, rect.Height));

        shell.Resize(1000, 700, 1.0);
        Assert.Equal((1000, 700), (rect.Width, rect.Height));

        shell.Apply(new ShellSettings(Background: "#ffffff"), new PanelLayout(24, 1, 2), KeyTable.Build([], []));
        Assert.Equal(new RenderColor(1f, 1f, 1f, 1f), rect.Color);
    }

    [Fact]
    public void A_rotated_resize_lays_out_in_the_swapped_logical_size_and_the_panel_strips_follow()
    {
        using var harness = new NestedShellHarness();
        var shell = harness.Shell;
        var toplevel = harness.MapToplevel(width: 200, height: 150, serverDecorated: true);
        var rect = Assert.IsType<SceneRect>(Assert.Single(shell.Layers.Background.Children));
        Assert.Equal(OutputTransform.Normal, shell.Transform);
        Assert.Equal(new Box(0, 0, 800, 600), shell.Output);

        shell.Resize(800, 600, 1.0, OutputTransform.Rotate90);
        Assert.Equal(OutputTransform.Rotate90, shell.Transform);
        Assert.Equal(OutputTransform.Rotate90, shell.View.Output.Transform);
        Assert.Equal((800, 600), (shell.View.Output.CurrentMode.Width, shell.View.Output.CurrentMode.Height));
        Assert.Equal(new Box(0, 0, 600, 800), shell.Output);
        Assert.Equal((600, 800), (rect.Width, rect.Height));
        Assert.Equal(new Box(0, 0, 600, 24), shell.TopStrip);
        Assert.Equal(new Box(0, 800 - 24, 600, 24), shell.BottomStrip);
        Assert.Equal(new Box(0, 24, 600, 800 - 48), shell.WorkArea);
        var screen = Assert.Single(harness.Host.Screens.Current);
        Assert.Equal((600, 800, OutputTransform.Rotate90), (screen.Width, screen.Height, screen.Transform));
        var window = Assert.Single(shell.Windows);
        Assert.True(window.FrameBox.Right <= 600, "the window was left outside the rotated output");

        shell.Resize(800, 600, 1.0);
        Assert.Equal(new Box(0, 0, 800, 600), shell.Output);
        Assert.Equal(OutputTransform.Normal, shell.View.Output.Transform);
        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [Fact]
    public void An_edge_resize_stops_at_the_panels()
    {
        using var harness = new NestedShellHarness();
        var shell = harness.Shell;
        var toplevel = harness.MapToplevel(width: 200, height: 150, title: "sized", serverDecorated: true);
        var window = shell.Windows[0];
        window.MoveFrameTo(window.FrameBox.X, shell.WorkArea.Y + 100);
        var before = window.FrameBox;
        var center = before.X + (before.Width / 2);

        Move(shell, center, before.Y);
        shell.BeginResize(window, ResizeEdges.Top, null);
        Move(shell, center, -100);
        CommitConfigured(harness, toplevel, window);
        Button(shell, 0x110, false);
        harness.PumpInput();
        Assert.Equal(shell.WorkArea.Y, window.FrameBox.Y);
        Assert.Equal(before.Bottom, window.FrameBox.Bottom);

        var top = window.FrameBox;
        Move(shell, center, top.Bottom);
        shell.BeginResize(window, ResizeEdges.Bottom, null);
        Move(shell, center, shell.Output.Bottom + 100);
        CommitConfigured(harness, toplevel, window);
        Button(shell, 0x110, false);
        harness.PumpInput();
        Assert.Equal(shell.WorkArea.Bottom, window.FrameBox.Bottom);
        Assert.Equal(top.Y, window.FrameBox.Y);

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [Fact]
    public void A_title_drag_moves_the_window_and_a_click_on_another_focuses_it()
    {
        using var harness = new NestedShellHarness();
        var shell = harness.Shell;
        var first = harness.MapToplevel(width: 200, height: 150, title: "first", serverDecorated: true);
        var second = harness.MapToplevel(width: 200, height: 150, title: "second", serverDecorated: true);
        var one = shell.Windows[0];
        var two = shell.Windows[1];
        Assert.Same(two, shell.Focused);
        Assert.True(two.FrameBox.X >= one.FrameBox.Right || two.FrameBox.Y >= one.FrameBox.Bottom, "the second window overlaps the first");

        var before = one.FrameBox;
        var titleX = before.X + (before.Width / 2);
        var titleY = before.Y + (one.Insets.Top / 2);
        Move(shell, titleX, titleY);
        Button(shell, 0x110, true);
        harness.PumpInput();
        Assert.Same(one, shell.Focused);
        Move(shell, titleX + 40, titleY + 30);
        Button(shell, 0x110, false);
        harness.PumpInput();
        Assert.Equal(before.X + 40, one.FrameBox.X);
        Assert.Equal(before.Y + 30, one.FrameBox.Y);

        var client = two.ClientBox;
        Move(shell, client.X + 5, client.Y + 5);
        Button(shell, 0x110, true);
        Button(shell, 0x110, false);
        harness.PumpInput();
        Assert.Same(two, shell.Focused);

        var top = one.FrameBox;
        Move(shell, top.X + (top.Width / 2), top.Y + (one.Insets.Top / 2));
        Button(shell, 0x110, true);
        Move(shell, top.X + (top.Width / 2), 2);
        Assert.Equal(shell.WorkArea.Y, one.FrameBox.Y);
        Move(shell, top.X + (top.Width / 2), shell.Output.Bottom + 200);
        Assert.True(one.FrameBox.Y + one.Insets.Top <= shell.WorkArea.Bottom, "the title bar left the work area at the bottom");
        Button(shell, 0x110, false);
        harness.PumpInput();

        first.Destroy();
        second.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed windows outlived their toplevels");
    }

    [Fact]
    public void Alt_tab_switches_focus_and_the_workspace_chords_move_between_workspaces()
    {
        using var harness = new NestedShellHarness();
        var shell = harness.Shell;
        var first = harness.MapToplevel(title: "first", serverDecorated: true);
        var second = harness.MapToplevel(title: "second", serverDecorated: true);
        var one = shell.Windows[0];
        var two = shell.Windows[1];
        Assert.Same(two, shell.Focused);

        var shown = 0;
        var hidden = 0;
        shell.SwitcherShown += (_, _) => shown++;
        shell.SwitcherHidden += () => hidden++;
        Key(shell, 56, true);
        Key(shell, 15, true);
        Key(shell, 15, false);
        Assert.True(shell.SwitcherOpen);
        Assert.Equal(1, shown);
        Key(shell, 56, false);
        harness.PumpInput();
        Assert.False(shell.SwitcherOpen);
        Assert.Equal(1, hidden);
        Assert.Same(one, shell.Focused);

        Key(shell, 29, true);
        Key(shell, 56, true);
        Key(shell, 106, true);
        Key(shell, 106, false);
        Key(shell, 56, false);
        Key(shell, 29, false);
        harness.PumpInput();
        Assert.Equal(1, shell.Workspaces.Current);
        Assert.False(one.Tree!.Enabled);
        Assert.False(two.Tree!.Enabled);
        Assert.Null(shell.Focused);

        Key(shell, 29, true);
        Key(shell, 56, true);
        Key(shell, 105, true);
        Key(shell, 105, false);
        Key(shell, 56, false);
        Key(shell, 29, false);
        harness.PumpInput();
        Assert.Equal(0, shell.Workspaces.Current);
        Assert.True(one.Tree!.Enabled);
        Assert.Same(one, shell.Focused);

        first.Destroy();
        second.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed windows outlived their toplevels");
    }

    [Fact]
    public void A_client_decorated_window_that_drops_its_shadow_when_maximized_stays_pinned_to_the_work_area()
    {
        using var harness = new NestedShellHarness();
        var shell = harness.Shell;
        var toplevel = harness.MapToplevel(width: 250, height: 200, title: "csd");
        var window = Assert.Single(shell.Windows);
        Assert.False(window.Decorated);

        toplevel.XdgSurface.SetWindowGeometry(25, 25, 200, 150);
        toplevel.Surface.Commit();
        harness.PumpUntil(() => window.Geometry.X == 25, "the geometry offset never reached the shell");
        var before = window.ClientBox;
        Assert.Equal(25, window.Geometry.X);

        shell.SetMaximized(window, true);
        harness.PumpUntil(() => window.Maximized && toplevel.ConfiguredWidth == shell.WorkArea.Width, "the window never maximized");
        var width = toplevel.ConfiguredWidth;
        var height = toplevel.ConfiguredHeight;
        var buffer = harness.Client.CreateBuffer(width, height, NestedShellHarness.Fill(width, height, 0xFF3366AA));
        toplevel.XdgSurface.SetWindowGeometry(0, 0, width, height);
        toplevel.Surface.Attach(buffer.Proxy, 0, 0);
        toplevel.Surface.Damage(0, 0, width, height);
        toplevel.Surface.Commit();
        harness.PumpUntil(() => window.Geometry.X == 0 && window.Geometry.Width == width, "the maximized buffer never arrived");

        Assert.Equal(shell.WorkArea, window.ClientBox);
        Assert.True(before.X >= 0 && before.Y >= shell.WorkArea.Y);

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [Fact]
    public void The_panel_snapshot_follows_the_frame_once_a_maximized_or_restored_buffer_arrives()
    {
        using var harness = new NestedShellHarness();
        var shell = harness.Shell;
        var toplevel = harness.MapToplevel(width: 200, height: 150, title: "notes", serverDecorated: true);
        var window = Assert.Single(shell.Windows);
        var restored = window.FrameBox;
        Box Snapshot() => Assert.Single(shell.SnapshotWindows()).Frame;
        Assert.Equal(restored, Snapshot());

        var published = 0;
        shell.Changed += () => published++;
        shell.SetMaximized(window, true);
        harness.PumpUntil(() => toplevel.ConfiguredWidth > 200, "the window never maximized");
        var publishedBeforeCommit = published;
        harness.Commit(toplevel, toplevel.ConfiguredWidth, toplevel.ConfiguredHeight);
        harness.PumpUntil(() => window.Geometry.Width == toplevel.ConfiguredWidth, "the maximized buffer never arrived");
        Assert.True(published > publishedBeforeCommit, "the committed maximized size was not published");
        Assert.Equal(shell.WorkArea, Snapshot());

        shell.SetMaximized(window, false);
        harness.PumpUntil(() => toplevel.ConfiguredWidth == 200, "the window never restored");
        publishedBeforeCommit = published;
        harness.Commit(toplevel, 200, 150);
        harness.PumpUntil(() => window.Geometry.Width == 200, "the restored buffer never arrived");
        Assert.True(published > publishedBeforeCommit, "the committed restored size was not published");
        Assert.Equal(restored, Snapshot());
        Assert.Equal(restored, window.FrameBox);

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [Fact]
    public void The_window_suffix_decorates_the_title_and_the_snapshot_and_follows_a_change()
    {
        using var harness = new NestedShellHarness();
        var shell = harness.Shell;
        var toplevel = harness.MapToplevel(title: "notes", serverDecorated: true);
        var window = Assert.Single(shell.Windows);
        Assert.Null(window.Suffix);
        Assert.Equal("notes", window.DecoratedTitle);
        Assert.Null(Assert.Single(shell.SnapshotWindows()).Suffix);

        shell.WindowSuffix = client => ReferenceEquals(client, window.Client) ? "box" : null;
        Assert.Equal("box", window.Suffix);
        Assert.Equal("notes — box", window.DecoratedTitle);
        Assert.Equal("box", Assert.Single(shell.SnapshotWindows()).Suffix);

        shell.WindowSuffix = null;
        Assert.Equal("notes", window.DecoratedTitle);

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [Fact]
    public void The_host_full_screen_chord_is_the_shells_own_and_reaches_the_window()
    {
        using var harness = new NestedShellHarness();
        var shell = harness.Shell;
        var toplevel = harness.MapToplevel(title: "notes", serverDecorated: true);
        var requests = 0;
        shell.HostFullScreenRequested += () => requests++;

        Key(shell, 29, true);
        Key(shell, 56, true);
        Key(shell, 28, true);
        Key(shell, 28, false);
        Key(shell, 56, false);
        Key(shell, 29, false);
        Assert.Equal(1, requests);

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    [Fact]
    public void Toggle_maximized_fills_the_work_area_and_restores()
    {
        using var harness = new NestedShellHarness();
        var shell = harness.Shell;
        var toplevel = harness.MapToplevel(width: 200, height: 150, title: "notes", serverDecorated: true);
        var window = Assert.Single(shell.Windows);
        var before = window.FrameBox;

        Key(shell, 56, true);
        Key(shell, 68, true);
        Key(shell, 68, false);
        Key(shell, 56, false);
        harness.PumpUntil(() => window.Maximized && toplevel.ConfiguredWidth > 200, "the window never maximized");
        var insets = window.Insets;
        Assert.Equal(shell.WorkArea.Width - insets.Left - insets.Right, toplevel.ConfiguredWidth);
        Assert.Equal(shell.WorkArea.Height - insets.Top - insets.Bottom, toplevel.ConfiguredHeight);
        Assert.Equal(shell.WorkArea.X, window.FrameBox.X);
        Assert.Equal(shell.WorkArea.Y, window.FrameBox.Y);

        Key(shell, 56, true);
        Key(shell, 68, true);
        Key(shell, 68, false);
        Key(shell, 56, false);
        harness.PumpUntil(() => !window.Maximized, "the window never restored");
        Assert.Equal(before.X, window.FrameBox.X);
        Assert.Equal(before.Y, window.FrameBox.Y);

        toplevel.Destroy();
        harness.PumpUntil(() => shell.Windows.Count == 0, "the managed window outlived its toplevel");
    }

    private static void CommitConfigured(NestedShellHarness harness, HarnessToplevel toplevel, ManagedWindow window)
    {
        var height = window.Geometry.Height;
        harness.PumpUntil(() => toplevel.ConfiguredHeight != 0 && toplevel.ConfiguredHeight != height, "the resize never reached the client");
        var width = toplevel.ConfiguredWidth;
        height = toplevel.ConfiguredHeight;
        harness.Commit(toplevel, width, height);
        harness.PumpUntil(() => window.Geometry.Height == height, "the resized buffer never arrived");
    }

    private static void Move(NestedShell shell, double x, double y) => NestedShellHarness.Move(shell, x, y);

    private static void Button(NestedShell shell, uint code, bool pressed) => NestedShellHarness.Button(shell, code, pressed);

    private static void Key(NestedShell shell, uint code, bool pressed) => NestedShellHarness.Key(shell, code, pressed);

    private static unsafe uint Pixel(in BufferDataView view, int x, int y) =>
        *(uint*)((byte*)view.Data + (y * view.Stride) + (x * 4));
}
