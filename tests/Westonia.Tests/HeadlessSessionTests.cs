using System.Diagnostics;
using System.Text;
using Westonia.Shell;
using Xunit;

namespace Westonia.Tests;

public sealed class HeadlessSessionTests
{
    [Fact]
    public async Task A_real_client_maps_is_framed_and_leaves_nothing_behind()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor half is Linux only");
        var compositor = Locate("westonia");
        Assert.SkipWhen(compositor is null, "westonia has not been built beside the tests");
        var client = Which("weston-simple-shm");
        Assert.SkipWhen(client is null, "weston-simple-shm is not installed");

        using var session = new Session(compositor!);
        var socket = await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal));
        Assert.NotNull(socket);
        var display = socket!.Split(' ')[1];

        var keymap = await session.WaitForAsync(line => line.StartsWith("KEYMAP ", StringComparison.Ordinal));
        Assert.NotNull(keymap);
        Assert.Contains("compiled=yes", keymap!, StringComparison.Ordinal);

        using var app = Start(client!, display);
        var window = await session.WaitForAsync(
            line => line.StartsWith("WINDOW ", StringComparison.Ordinal),
            poke: "where");
        Assert.NotNull(window);
        Assert.Contains("focused=True", window!, StringComparison.Ordinal);
        Assert.Contains("ws=1", window, StringComparison.Ordinal);

        if (!app.HasExited)
        {
            app.Kill(entireProcessTree: true);
        }

        app.WaitForExit(5000);
        await session.SettleAsync();
        await session.SendAsync("quit");
        var frames = await session.WaitForAsync(line => line.StartsWith("FRAMES ", StringComparison.Ordinal));
        Assert.NotNull(frames);

        var live = frames!.Split(' ');
        Assert.Equal("LIVE", live[2]);
        if (live[3] != "untracked")
        {
            Assert.Equal("0", live[3]);
        }
    }

    [Fact]
    public async Task A_client_that_maps_fullscreen_owns_the_output_and_comes_back_from_it()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor half is Linux only");
        var compositor = Locate("westonia");
        Assert.SkipWhen(compositor is null, "westonia has not been built beside the tests");
        var client = Which("foot");
        Assert.SkipWhen(client is null, "foot is not installed");

        using var session = new Session(compositor!);
        var socket = await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal));
        Assert.NotNull(socket);

        using var app = Start(client!, socket!.Split(' ')[1], "--fullscreen");
        var mapped = await session.WaitForAsync(
            line => line.StartsWith("WINDOW ", StringComparison.Ordinal),
            poke: "where");
        Assert.NotNull(mapped);
        var area = await session.WaitForAsync(line => line.StartsWith("AREA ", StringComparison.Ordinal));
        Assert.NotNull(area);

        Assert.Contains("fullscreen=True", mapped!, StringComparison.Ordinal);
        Assert.Equal(Field(area!, "X = "), Field(mapped, "X = "));
        Assert.Equal(Field(area, "Y = "), Field(mapped, "Y = "));
        Assert.Equal(Field(area, "Width = "), Field(mapped, "Width = "));
        Assert.Equal(Field(area, "Height = "), Field(mapped, "Height = "));

        await Chord(session, 33);
        var restored = await Geometry(session, holding: "fullscreen=False", until: line => Field(line, "Width = ") < Field(area, "Width = "));
        Assert.NotNull(restored);
        Assert.Contains("fullscreen=False", restored!, StringComparison.Ordinal);
        Assert.Contains("kind=Normal", restored, StringComparison.Ordinal);
        Assert.True(Field(restored, "Width = ") < Field(area, "Width = "), restored);
        Assert.True(Field(restored, "Y = ") > Field(area, "Y = "), restored);

        if (!app.HasExited)
        {
            app.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task A_state_a_client_unsets_before_it_maps_does_not_turn_into_that_state()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor half is Linux only");
        var compositor = Locate("westonia");
        Assert.SkipWhen(compositor is null, "westonia has not been built beside the tests");
        var client = Which("epiphany");
        Assert.SkipWhen(client is null, "epiphany is not installed");

        using var session = new Session(compositor!);
        var socket = await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal));
        Assert.NotNull(socket);

        Environment.SetEnvironmentVariable("GTK_A11Y", "none");
        var profile = Path.Combine(Path.GetTempPath(), $"westonia-tests-{Environment.ProcessId}-epiphany");
        Directory.CreateDirectory(profile);
        try
        {
            using var app = Start(client!, socket!.Split(' ')[1], $"--profile={profile}", "about:blank");
            var mapped = await session.WaitForAsync(
                line => line.StartsWith("WINDOW ", StringComparison.Ordinal),
                poke: "where");
            Assert.NotNull(mapped);
            Assert.Contains("fullscreen=False", mapped!, StringComparison.Ordinal);
            Assert.Contains("maximized=False", mapped, StringComparison.Ordinal);
            Assert.Contains("kind=Normal", mapped, StringComparison.Ordinal);

            if (!app.HasExited)
            {
                app.Kill(entireProcessTree: true);
            }
        }
        finally
        {
            Directory.Delete(profile, recursive: true);
        }
    }

    [Fact]
    public async Task A_maximized_window_refuses_a_move_and_moves_again_once_it_is_restored()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor half is Linux only");
        var compositor = Locate("westonia");
        Assert.SkipWhen(compositor is null, "westonia has not been built beside the tests");
        var client = Which("foot");
        Assert.SkipWhen(client is null, "foot is not installed");

        using var session = new Session(compositor!);
        var socket = await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal));
        Assert.NotNull(socket);

        using var app = Start(client!, socket!.Split(' ')[1]);
        var mapped = await session.WaitForAsync(
            line => line.StartsWith("WINDOW ", StringComparison.Ordinal),
            poke: "where");
        Assert.NotNull(mapped);

        await Chord(session, 50);
        var maximized = await Geometry(session, holding: "maximized=True");
        Assert.NotNull(maximized);
        Assert.Contains("maximized=True", maximized!, StringComparison.Ordinal);

        await ModifierDrag(session, 600, 400, 700, 500, grab: false);
        var held = await Geometry(session);
        Assert.NotNull(held);
        Assert.Contains("maximized=True", held!, StringComparison.Ordinal);
        Assert.Equal(Field(maximized, "X = "), Field(held, "X = "));
        Assert.Equal(Field(maximized, "Y = "), Field(held, "Y = "));

        await Chord(session, 50);
        var restored = await Geometry(session, holding: "maximized=False");
        Assert.NotNull(restored);
        Assert.Contains("maximized=False", restored!, StringComparison.Ordinal);

        await ModifierDrag(session, 600, 400, 700, 500);
        var moved = await Geometry(session, changedFrom: restored);
        Assert.NotNull(moved);
        Assert.Equal(Field(restored, "X = ") + 100, Field(moved, "X = "));
        Assert.Equal(Field(restored, "Y = ") + 100, Field(moved, "Y = "));

        if (!app.HasExited)
        {
            app.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task A_maximized_window_refuses_a_resize_and_resizes_again_once_it_is_restored()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor half is Linux only");
        var compositor = Locate("westonia");
        Assert.SkipWhen(compositor is null, "westonia has not been built beside the tests");
        var client = Which("foot");
        Assert.SkipWhen(client is null, "foot is not installed");

        using var session = new Session(compositor!);
        var socket = await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal));
        Assert.NotNull(socket);

        using var app = Start(client!, socket!.Split(' ')[1]);
        var mapped = await session.WaitForAsync(
            line => line.StartsWith("WINDOW ", StringComparison.Ordinal),
            poke: "where");
        Assert.NotNull(mapped);

        await Chord(session, 50);
        var maximized = await Geometry(session, holding: "maximized=True");
        Assert.NotNull(maximized);
        Assert.Contains("maximized=True", maximized!, StringComparison.Ordinal);

        await ModifierDrag(session, 900, 600, 700, 400, button: 273, grab: false);
        var held = await Geometry(session);
        Assert.NotNull(held);
        Assert.Equal(Field(maximized, "Width = "), Field(held!, "Width = "));
        Assert.Equal(Field(maximized, "Height = "), Field(held, "Height = "));

        await Chord(session, 50);
        var restored = await Geometry(session, holding: "maximized=False");
        Assert.NotNull(restored);
        Assert.Contains("maximized=False", restored!, StringComparison.Ordinal);

        await ModifierDrag(session, 700, 400, 900, 600, button: 273);
        var resized = await Geometry(session, changedFrom: restored);
        Assert.NotNull(resized);
        Assert.True(Field(resized!, "Width = ") > Field(restored, "Width = "));
        Assert.True(Field(resized, "Height = ") > Field(restored, "Height = "));

        if (!app.HasExited)
        {
            app.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task A_move_takes_the_window_out_of_its_tiled_orientation()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor half is Linux only");
        var compositor = Locate("westonia");
        Assert.SkipWhen(compositor is null, "westonia has not been built beside the tests");
        var client = Which("foot");
        Assert.SkipWhen(client is null, "foot is not installed");

        using var session = new Session(compositor!);
        var socket = await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal));
        Assert.NotNull(socket);

        using var app = Start(client!, socket!.Split(' ')[1]);
        var mapped = await session.WaitForAsync(
            line => line.StartsWith("WINDOW ", StringComparison.Ordinal),
            poke: "where");
        Assert.NotNull(mapped);

        await Chord(session, 105);
        var tiled = await Geometry(session, holding: "tiled=Left");
        Assert.NotNull(tiled);
        Assert.Contains("tiled=Left", tiled!, StringComparison.Ordinal);

        await ModifierDrag(session, 300, 400, 400, 500);
        var moved = await Geometry(session, holding: "tiled=None");
        Assert.NotNull(moved);
        Assert.Contains("tiled=None", moved!, StringComparison.Ordinal);
        Assert.Equal(Field(tiled, "X = ") + 100, Field(moved, "X = "));

        if (!app.HasExited)
        {
            app.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task A_drag_that_ends_on_other_chrome_leaves_no_button_held()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor half is Linux only");
        var compositor = Locate("westonia");
        Assert.SkipWhen(compositor is null, "westonia has not been built beside the tests");
        var client = Which("foot");
        Assert.SkipWhen(client is null, "foot is not installed");

        using var session = new Session(compositor!);
        var socket = await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal));
        Assert.NotNull(socket);

        using var app = Start(client!, socket!.Split(' ')[1]);
        var placed = await session.WaitForAsync(
            line => line.StartsWith("WINDOW ", StringComparison.Ordinal),
            poke: "where");
        Assert.NotNull(placed);

        await session.SendAsync("move 600 400");
        await session.SendAsync("button 272 1");
        await session.SendAsync("move 600 114");
        await session.SendAsync("button 272 0");
        await session.SettleAsync();

        var grab = await session.WaitForAsync(
            line => line.StartsWith("GRAB ", StringComparison.Ordinal),
            poke: "where");
        Assert.NotNull(grab);
        Assert.Contains("buttons=False", grab!, StringComparison.Ordinal);

        var before = await Geometry(session);
        await session.SendAsync("move 600 114");
        await session.SendAsync("button 272 1");
        await session.SendAsync("move 700 214");
        await session.SendAsync("button 272 0");
        await session.SettleAsync();

        var after = await Geometry(session, changedFrom: before);
        Assert.NotEqual(before, after);

        if (!app.HasExited)
        {
            app.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task A_client_that_asks_to_be_moved_can_ask_again()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor half is Linux only");
        var compositor = Locate("westonia");
        Assert.SkipWhen(compositor is null, "westonia has not been built beside the tests");
        var client = Which("weston-flower");
        Assert.SkipWhen(client is null, "weston-flower is not installed");

        using var session = new Session(compositor!);
        var socket = await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal));
        Assert.NotNull(socket);

        using var app = Start(client!, socket!.Split(' ')[1]);
        var mapped = await session.WaitForAsync(
            line => line.StartsWith("WINDOW ", StringComparison.Ordinal),
            poke: "where");
        Assert.NotNull(mapped);

        var first = await DragAndRead(session, 620, 350, 700, 430);
        var second = await DragAndRead(session, 700, 430, 780, 510);
        var third = await DragAndRead(session, 780, 510, 600, 400);

        Assert.NotEqual(mapped, first);
        Assert.NotEqual(first, second);
        Assert.NotEqual(second, third);

        var grab = await session.WaitForAsync(
            line => line.StartsWith("GRAB ", StringComparison.Ordinal),
            poke: "where",
            fresh: true);
        Assert.NotNull(grab);
        Assert.Contains("buttons=False", grab!, StringComparison.Ordinal);

        if (!app.HasExited)
        {
            app.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task A_window_dragged_upwards_stops_at_the_panel()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor half is Linux only");
        var compositor = Locate("westonia");
        Assert.SkipWhen(compositor is null, "westonia has not been built beside the tests");
        var client = Which("foot");
        Assert.SkipWhen(client is null, "foot is not installed");

        const int safety = 50;

        using var session = new Session(compositor!);
        var socket = await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal));
        Assert.NotNull(socket);

        using var app = Start(client!, socket!.Split(' ')[1]);
        var placed = await session.WaitForAsync(
            line => line.StartsWith("WINDOW ", StringComparison.Ordinal),
            poke: "where");
        Assert.NotNull(placed);

        var area = await session.WaitForAsync(
            line => line.StartsWith("AREA ", StringComparison.Ordinal),
            poke: "where",
            fresh: true);
        Assert.NotNull(area);
        var work = Field(area![area.IndexOf("work=", StringComparison.Ordinal)..], "Y = ");

        var x = Field(placed!, "X = ");
        var y = Field(placed!, "Y = ");
        var height = Field(placed!, "Height = ");

        await Drag(session, x + 100, y - (FrameModel.TitlebarHeight / 2), x + 100, 0);
        var clamped = await Geometry(session);
        Assert.NotNull(clamped);
        Assert.Equal(work + FrameModel.TitlebarHeight, Field(clamped!, "Y = "));

        y = Field(clamped!, "Y = ");
        await session.SendAsync("key 125 1");
        await session.SettleAsync();
        await Drag(session, x + 100, y + height - 20, x + 100, 0);
        await session.SendAsync("key 125 0");
        await session.SettleAsync();

        var pushed = await Geometry(session);
        Assert.NotNull(pushed);
        Assert.Equal(
            work + safety,
            Field(pushed!, "Y = ") + height + FrameModel.BorderWidth);

        if (!app.HasExited)
        {
            app.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task A_resize_from_the_top_left_leaves_the_far_corner_alone()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor half is Linux only");
        var compositor = Locate("westonia");
        Assert.SkipWhen(compositor is null, "westonia has not been built beside the tests");
        var client = Which("foot");
        Assert.SkipWhen(client is null, "foot is not installed");

        using var session = new Session(compositor!);
        var socket = await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal));
        Assert.NotNull(socket);

        using var app = Start(client!, socket!.Split(' ')[1]);
        var placed = await Geometry(session);
        Assert.NotNull(placed);

        var x = Field(placed!, "X = ");
        var y = Field(placed!, "Y = ");
        var right = Field(placed!, "Right = ");
        var bottom = Field(placed!, "Bottom = ");

        var grabX = x - (FrameModel.BorderWidth / 2);
        var grabY = y - FrameModel.TitlebarHeight + 2;
        await Drag(session, grabX, grabY, grabX - 60, grabY - 60);

        var resized = await Geometry(session, changedFrom: placed);
        Assert.NotNull(resized);
        Assert.NotEqual(placed, resized);
        Assert.Equal(right, Field(resized!, "Right = "));
        Assert.Equal(bottom, Field(resized!, "Bottom = "));

        if (!app.HasExited)
        {
            app.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task A_click_through_the_frames_shadow_reaches_the_window_below()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor half is Linux only");
        var compositor = Locate("westonia");
        Assert.SkipWhen(compositor is null, "westonia has not been built beside the tests");
        var client = Which("foot");
        Assert.SkipWhen(client is null, "foot is not installed");

        using var session = new Session(compositor!);
        var socket = await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal));
        Assert.NotNull(socket);
        var display = socket!.Split(' ')[1];

        using var below = Start(client!, display, "--title=below", "sleep", "300");
        Assert.NotNull(await session.WaitForAsync(
            line => line.StartsWith("WINDOW \"below\"", StringComparison.Ordinal),
            poke: "where"));

        using var above = Start(
            client!, display, "--title=above", "--window-size-pixels=400x300", "sleep", "300");
        var top = await session.WaitForAsync(
            line => line.StartsWith("WINDOW \"above\"", StringComparison.Ordinal) &&
                line.Contains("focused=True", StringComparison.Ordinal),
            poke: "where");
        Assert.NotNull(top);

        var x = Field(top!, "X = ") - FrameModel.Margin - FrameModel.BorderWidth + (FrameModel.Margin / 2);
        var y = Field(top!, "Y = ") + (Field(top!, "Height = ") / 2);
        await session.SendAsync($"move {x} {y}");
        await session.SettleAsync();
        await session.SendAsync("button 272 1");
        await session.SettleAsync();
        await session.SendAsync("button 272 0");
        await session.SettleAsync();

        var raised = await session.WaitForAsync(
            line => line.StartsWith("WINDOW \"below\"", StringComparison.Ordinal),
            poke: "where",
            fresh: true);
        Assert.NotNull(raised);
        Assert.Contains("focused=True", raised!, StringComparison.Ordinal);

        foreach (var window in new[] { above, below })
        {
            if (!window.HasExited)
            {
                window.Kill(entireProcessTree: true);
            }
        }
    }

    [Fact]
    public async Task A_window_leaving_fullscreen_returns_to_its_own_workspace()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor half is Linux only");
        var compositor = Locate("westonia");
        Assert.SkipWhen(compositor is null, "westonia has not been built beside the tests");
        var client = Which("foot");
        Assert.SkipWhen(client is null, "foot is not installed");

        var directory = Directory.CreateTempSubdirectory("westonia-workspaces");
        var config = Path.Combine(directory.FullName, "weston.ini");
        await File.WriteAllTextAsync(
            config,
            "[shell]\nnum-workspaces=2\n",
            TestContext.Current.CancellationToken);

        try
        {
            using var session = new Session(compositor!, config);
            var socket = await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal));
            Assert.NotNull(socket);

            using var app = Start(client!, socket!.Split(' ')[1]);
            Assert.NotNull(await session.WaitForAsync(
                line => line.StartsWith("WINDOW ", StringComparison.Ordinal),
                poke: "where"));

            await Chord(session, 125, 42, 33);
            var full = await session.WaitForAsync(
                line => line.StartsWith("WINDOW ", StringComparison.Ordinal),
                poke: "where",
                fresh: true);
            Assert.NotNull(full);
            Assert.Contains("kind=Fullscreen", full!, StringComparison.Ordinal);

            await Chord(session, 125, 108);
            var workspace = await session.WaitForAsync(
                line => line.StartsWith("WORKSPACE ", StringComparison.Ordinal),
                poke: "where",
                fresh: true);
            Assert.NotNull(workspace);
            Assert.StartsWith("WORKSPACE 2/2", workspace!, StringComparison.Ordinal);

            await Chord(session, 125, 42, 33);
            var restored = await session.WaitForAsync(
                line => line.StartsWith("WINDOW ", StringComparison.Ordinal),
                poke: "where",
                fresh: true);
            Assert.NotNull(restored);
            Assert.Contains("kind=Normal", restored!, StringComparison.Ordinal);
            Assert.Contains("ws=1", restored!, StringComparison.Ordinal);

            await session.SendAsync("move 640 360");
            await session.SettleAsync();
            var hit = await session.WaitForAsync(
                line => line.StartsWith("HIT ", StringComparison.Ordinal),
                poke: "where",
                fresh: true);
            Assert.NotNull(hit);
            Assert.StartsWith("HIT scene=none", hit!, StringComparison.Ordinal);

            if (!app.HasExited)
            {
                app.Kill(entireProcessTree: true);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_virtual_pointer_moves_the_pointer()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor half is Linux only");
        var compositor = Locate("westonia");
        Assert.SkipWhen(compositor is null, "westonia has not been built beside the tests");
        var vptr = WlClient("vptr");
        Assert.SkipWhen(vptr is null, "scripts/wlclients/bin/vptr has not been built");

        using var session = new Session(compositor!);
        var socket = await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal));
        Assert.NotNull(socket);
        var before = await session.WaitForAsync(line => line.StartsWith("POINTER ", StringComparison.Ordinal), poke: "where");
        Assert.NotNull(before);
        var output = new List<string>();
        using (var client = StartListening(vptr!, socket!.Split(' ')[1], output, "25", "50", "100", "100", "0"))
        {
            client.WaitForExit(10_000);
        }

        var after = await session.WaitForAsync(line => line.StartsWith("POINTER ", StringComparison.Ordinal) && line != before, poke: "where", fresh: true);
        Assert.True(after is not null, $"the pointer stayed at {before}");
        Assert.Equal("POINTER 320 360", after);
        await session.SendAsync("quit");
    }

    [Fact]
    public async Task A_layer_shell_panel_maps_takes_the_keyboard_and_shrinks_the_work_area()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor half is Linux only");
        var compositor = Locate("westonia");
        Assert.SkipWhen(compositor is null, "westonia has not been built beside the tests");
        var panel = WlClient("panel");
        Assert.SkipWhen(panel is null, "scripts/wlclients/bin/panel has not been built");

        using var session = new Session(compositor!);
        var socket = await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal));
        Assert.NotNull(socket);
        var output = new List<string>();
        using (var client = StartListening(panel!, socket!.Split(' ')[1], output, "bottom", "40", "keyboard"))
        {
            Assert.True(await WaitForLineAsync(output, "PANEL 1280x40"), "the panel was never configured");
            Assert.NotNull(await session.WaitForAsync(line => line == "LAYER + panel Top"));
            Assert.True(await WaitForLineAsync(output, "KEYBOARD enter"), "the panel did not get the keyboard");
            await session.SendAsync("key 30 1");
            await session.SendAsync("key 30 0");
            Assert.True(await WaitForLineAsync(output, "KEY 30 1"), "the key did not reach the panel");
            var area = await session.WaitForAsync(line => line.StartsWith("AREA ", StringComparison.Ordinal), poke: "where", fresh: true);
            Assert.Contains("work=Box { X = 0, Y = 32, Width = 1280, Height = 648,", area!, StringComparison.Ordinal);
            client.Kill(entireProcessTree: true);
        }

        Assert.NotNull(await session.WaitForAsync(line => line == "LAYER - panel"));
        var restored = await session.WaitForAsync(line => line.StartsWith("AREA ", StringComparison.Ordinal), poke: "where", fresh: true);
        Assert.Contains("work=Box { X = 0, Y = 32, Width = 1280, Height = 688,", restored!, StringComparison.Ordinal);
        await session.SendAsync("quit");
        var frames = await session.WaitForAsync(line => line.StartsWith("FRAMES ", StringComparison.Ordinal));
        Assert.NotNull(frames);
        var live = frames!.Split(' ');
        Assert.Equal("LIVE", live[2]);
        if (live[3] != "untracked")
        {
            Assert.Equal("0", live[3]);
        }
    }

    [Theory]
    [InlineData("gl")]
    [InlineData("vulkan")]
    public async Task Virtual_pointer_scroll_reaches_a_window_except_under_a_panel(string renderer)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor half is Linux only");
        Assert.SkipUnless(File.Exists("/dev/dri/renderD128"), "no render node for the inspector");
        var compositor = Locate("westonia");
        Assert.SkipWhen(compositor is null, "westonia has not been built beside the tests");

        using var session = new Session(compositor!, renderer: renderer);
        var socket = await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal));
        Assert.NotNull(socket);
        await session.SettleAsync();
        var (before, overPanel, overlay, after, where) = await Basin.Tests.InspectorScroll.Run(
            socket!.Split(' ')[1],
            (command, match) => session.WaitForAsync(match, poke: command, fresh: true),
            session.SendAsync,
            TestContext.Current.CancellationToken);
        Assert.True(before == 3 && overPanel == 0 && overlay == 3 && after == 3, $"{where}: before={before} over-panel={overPanel} overlay={overlay} after={after}");
        await session.SendAsync("quit");
    }

    [Theory]
    [InlineData("gl")]
    [InlineData("vulkan")]
    public async Task The_inspector_opens_takes_a_click_on_a_panel_and_closes(string renderer)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor half is Linux only");
        Assert.SkipUnless(File.Exists("/dev/dri/renderD128"), "no render node for the inspector");
        var compositor = Locate("westonia");
        Assert.SkipWhen(compositor is null, "westonia has not been built beside the tests");

        using var session = new Session(compositor!, renderer: renderer);
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));
        await session.SettleAsync();

        var mark = session.Mark();
        await session.SendAsync("inspector open");
        var opened = await session.WaitForAsync(line => line.StartsWith("INSPECTOR ", StringComparison.Ordinal), since: mark);
        Assert.NotNull(opened);
        Assert.SkipWhen(opened!.Contains("host=none", StringComparison.Ordinal), $"no inspector host: {opened}");
        Assert.SkipUnless(opened.Contains($"host={renderer}", StringComparison.Ordinal), $"the {renderer} row fell back: {opened}");
        Assert.StartsWith("INSPECTOR open=yes", opened, StringComparison.Ordinal);
        Assert.Contains("panels=Outputs", opened, StringComparison.Ordinal);

        await session.SendAsync("inspector-panel windows on");
        await session.SettleAsync();
        await Task.Delay(300, TestContext.Current.CancellationToken);
        await session.SendAsync("move 531 50");
        await Task.Delay(200, TestContext.Current.CancellationToken);
        await session.SendAsync("button 272 true");
        await session.SendAsync("button 272 false");
        await Task.Delay(300, TestContext.Current.CancellationToken);

        var state = await session.WaitForAsync(
            line => line.StartsWith("INSPECTOR open=yes", StringComparison.Ordinal) && !line.Contains("Outputs", StringComparison.Ordinal),
            poke: "inspector-state", fresh: true);
        Assert.NotNull(state);
        Assert.Contains("panels=Windows", state!, StringComparison.Ordinal);

        await session.SendAsync("inspector-panel outputs on");
        await Task.Delay(600, TestContext.Current.CancellationToken);
        await session.SendAsync("ipc input/touch {\"kind\":\"down\",\"id\":1,\"x\":531,\"y\":50}");
        await session.SendAsync("ipc input/touch {\"kind\":\"frame\"}");
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await session.SendAsync("ipc input/touch {\"kind\":\"up\",\"id\":1}");
        await session.SendAsync("ipc input/touch {\"kind\":\"frame\"}");
        await Task.Delay(400, TestContext.Current.CancellationToken);
        var touched = await session.WaitForAsync(
            line => line.StartsWith("INSPECTOR open=yes", StringComparison.Ordinal) && !line.Contains("Outputs", StringComparison.Ordinal),
            poke: "inspector-state", fresh: true);
        Assert.True(touched is not null, "a touch on the close box did not close Outputs");

        mark = session.Mark();
        await session.SendAsync("inspector close");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("INSPECTOR open=no", StringComparison.Ordinal), since: mark));

        await session.SendAsync("quit");
        var frames = await session.WaitForAsync(line => line.StartsWith("FRAMES ", StringComparison.Ordinal));
        Assert.NotNull(frames);
        var live = frames!.Split(' ');
        if (live[3] != "untracked")
        {
            Assert.Equal("0", live[3]);
        }
    }

    private static Process StartListening(string path, string display, List<string> lines, params string[] arguments)
    {
        var info = new ProcessStartInfo(path) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["WAYLAND_DISPLAY"] = display;
        var process = Process.Start(info)!;
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (lines)
                {
                    lines.Add(e.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task<bool> WaitForLineAsync(List<string> lines, string exact)
    {
        for (var i = 0; i < 500; i++)
        {
            lock (lines)
            {
                if (lines.Contains(exact))
                {
                    return true;
                }
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        return false;
    }

    private static string? WlClient(string name)
    {
        var directory = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && directory is not null; i++)
        {
            var candidate = Path.Combine(directory, "scripts", "wlclients", "bin", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }

    private static async Task Chord(Session session, params int[] codes)
    {
        foreach (var code in codes)
        {
            await session.SendAsync($"key {code} 1");
            await session.SettleAsync();
        }

        for (var i = codes.Length - 1; i >= 0; i--)
        {
            await session.SendAsync($"key {codes[i]} 0");
            await session.SettleAsync();
        }

        await session.SettleAsync();
    }

    private static int Field(string line, string name)
    {
        var start = line.IndexOf(name, StringComparison.Ordinal) + name.Length;
        var end = start;
        while (end < line.Length && (char.IsDigit(line[end]) || line[end] == '-'))
        {
            end++;
        }

        return int.Parse(line[start..end]);
    }

    private static async Task Drag(Session session, int sx, int sy, int ex, int ey)
    {
        var before = await Geometry(session);
        await session.SendAsync($"move {sx} {sy}");
        await session.SettleAsync();
        await session.SendAsync("button 272 1");
        await Grabbed(session);
        await session.SendAsync($"move {ex} {ey}");
        await Geometry(session, changedFrom: before);
        await session.SendAsync("button 272 0");
        await session.SettleAsync();
    }

    private static async Task<string?> DragAndRead(Session session, int sx, int sy, int ex, int ey)
    {
        var before = await Geometry(session);
        await session.SendAsync($"move {sx} {sy}");
        await session.SettleAsync();
        await session.SendAsync("button 272 1");
        Assert.NotNull(await session.WaitForAsync(
            line => line.StartsWith("GRAB kind=Move ", StringComparison.Ordinal),
            poke: "where",
            fresh: true));
        await session.SendAsync($"move {ex} {ey}");
        await session.SendAsync("button 272 0");
        await session.SettleAsync();
        var after = await Geometry(session, changedFrom: before);
        Assert.NotNull(after);
        return after;
    }

    private static async Task Chord(Session session, int key)
    {
        await session.SendAsync("key 125 1");
        await session.SendAsync("key 42 1");
        await session.SendAsync($"key {key} 1");
        await session.SendAsync($"key {key} 0");
        await session.SendAsync("key 42 0");
        await session.SendAsync("key 125 0");
        await session.SettleAsync();
    }

    private static async Task ModifierDrag(Session session, int sx, int sy, int ex, int ey, int button = 272, bool grab = true)
    {
        var before = await Geometry(session);
        await session.SendAsync("key 125 1");
        await session.SendAsync($"move {sx} {sy}");
        await session.SettleAsync();
        await session.SendAsync($"button {button} 1");
        await session.SettleAsync();
        if (grab)
        {
            await Grabbed(session);
        }

        await session.SendAsync($"move {ex} {ey}");
        if (grab)
        {
            await Geometry(session, changedFrom: before);
        }
        else
        {
            await session.SettleAsync();
        }

        await session.SendAsync($"button {button} 0");
        await session.SendAsync("key 125 0");
        await session.SettleAsync();
    }

    private static async Task Grabbed(Session session) =>
        Assert.NotNull(await session.WaitForAsync(
            line => line.StartsWith("GRAB kind=", StringComparison.Ordinal) &&
                    !line.StartsWith("GRAB kind=None ", StringComparison.Ordinal),
            poke: "where",
            fresh: true));

    private static async Task<string?> Geometry(Session session, string? changedFrom = null, string? holding = null, Func<string, bool>? until = null)
    {
        var deadline = Environment.TickCount64 + 10_000;
        string? previous = null;
        while (Environment.TickCount64 < deadline)
        {
            await session.SettleAsync();
            var line = await session.WaitForAsync(l => l.StartsWith("WINDOW ", StringComparison.Ordinal), poke: "where", fresh: true);
            if (line is not null && line == previous &&
                (changedFrom is null || line != changedFrom) &&
                (holding is null || line.Contains(holding, StringComparison.Ordinal)) &&
                (until is null || until(line)))
            {
                return line;
            }

            previous = line;
        }

        return null;
    }

    private static Process Start(string path, string display, params string[] arguments)
    {
        var info = new ProcessStartInfo(path) { UseShellExecute = false, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["WAYLAND_DISPLAY"] = display;
        return Process.Start(info)!;
    }

    private static string? Which(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(':'))
        {
            if (directory.Length > 0 && File.Exists(Path.Combine(directory, name)))
            {
                return Path.Combine(directory, name);
            }
        }

        return null;
    }

    private static string? Locate(string name)
    {
        var directory = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && directory is not null; i++)
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }

    private sealed class Session : IDisposable
    {
        private readonly Process _process;
        private readonly List<string> _lines = [];
        private readonly Lock _gate = new();
        private TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Session(string path, string config = "false", string renderer = "pixman")
        {
            var info = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            info.ArgumentList.Add("--backend");
            info.ArgumentList.Add("headless");
            info.ArgumentList.Add("--renderer");
            info.ArgumentList.Add(renderer);
            info.ArgumentList.Add("--config");
            info.ArgumentList.Add(config);
            info.Environment["BASIN_TRACE"] = string.Empty;

            _process = Process.Start(info)!;
            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    TaskCompletionSource arrived;
                    lock (_gate)
                    {
                        _lines.Add(e.Data);
                        arrived = _arrived;
                        _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    }

                    arrived.TrySetResult();
                }
            };
            _process.ErrorDataReceived += (_, _) => { };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public async Task SendAsync(string command)
        {
            await _process.StandardInput.WriteLineAsync(command);
            await _process.StandardInput.FlushAsync();
        }

        public int Mark()
        {
            lock (_gate)
            {
                return _lines.Count;
            }
        }

        public async Task SettleAsync()
        {
            var mark = Mark();
            await SendAsync("settle");
            var reply = await WaitForAsync(
                line => line.StartsWith("OK session/settle ", StringComparison.Ordinal) || line.StartsWith("ERR ", StringComparison.Ordinal),
                since: mark);
            Assert.True(reply is not null && reply.StartsWith("OK ", StringComparison.Ordinal), $"settle answered [{reply}]");
        }

        public async Task<string?> WaitForAsync(
            Func<string, bool> predicate,
            string? poke = null,
            bool fresh = false, int since = 0)
        {
            var floor = since;
            if (fresh)
            {
                lock (_gate)
                {
                    floor = _lines.Count;
                }
            }

            var deadline = Environment.TickCount64 + (100 * 100L);
            var cancel = TestContext.Current.CancellationToken;
            while (true)
            {
                if (poke is not null)
                {
                    await SendAsync(poke);
                }

                var slotEnd = poke is null ? deadline : Math.Min(deadline, Environment.TickCount64 + 100);
                while (true)
                {
                    Task next;
                    lock (_gate)
                    {
                        for (var j = _lines.Count - 1; j >= floor; j--)
                        {
                            if (predicate(_lines[j]))
                            {
                                return _lines[j];
                            }
                        }

                        next = _arrived.Task;
                    }

                    var remaining = slotEnd - Environment.TickCount64;
                    if (remaining <= 0)
                    {
                        break;
                    }

                    await Task.WhenAny(next, Task.Delay((int)remaining, cancel));
                    cancel.ThrowIfCancellationRequested();
                }

                if (Environment.TickCount64 >= deadline)
                {
                    return null;
                }
            }
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }

                _process.WaitForExit(5000);
            }
            catch (InvalidOperationException)
            {
            }

            _process.Dispose();
        }
    }
}
