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

        await Task.Delay(500, TestContext.Current.CancellationToken);
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
        var restored = await Geometry(session);
        Assert.NotNull(restored);
        Assert.Contains("fullscreen=False", restored!, StringComparison.Ordinal);
        Assert.Contains("kind=Normal", restored, StringComparison.Ordinal);
        Assert.True(Field(restored, "Width = ") < Field(area, "Width = "));
        Assert.True(Field(restored, "Y = ") > Field(area, "Y = "));

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
        var maximized = await Geometry(session);
        Assert.NotNull(maximized);
        Assert.Contains("maximized=True", maximized!, StringComparison.Ordinal);

        await ModifierDrag(session, 600, 400, 700, 500);
        var held = await Geometry(session);
        Assert.NotNull(held);
        Assert.Contains("maximized=True", held!, StringComparison.Ordinal);
        Assert.Equal(Field(maximized, "X = "), Field(held, "X = "));
        Assert.Equal(Field(maximized, "Y = "), Field(held, "Y = "));

        await Chord(session, 50);
        var restored = await Geometry(session);
        Assert.NotNull(restored);
        Assert.Contains("maximized=False", restored!, StringComparison.Ordinal);

        await ModifierDrag(session, 600, 400, 700, 500);
        var moved = await Geometry(session);
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
        var maximized = await Geometry(session);
        Assert.NotNull(maximized);
        Assert.Contains("maximized=True", maximized!, StringComparison.Ordinal);

        await ModifierDrag(session, 900, 600, 700, 400, button: 273);
        var held = await Geometry(session);
        Assert.NotNull(held);
        Assert.Equal(Field(maximized, "Width = "), Field(held!, "Width = "));
        Assert.Equal(Field(maximized, "Height = "), Field(held, "Height = "));

        await Chord(session, 50);
        var restored = await Geometry(session);
        Assert.NotNull(restored);
        Assert.Contains("maximized=False", restored!, StringComparison.Ordinal);

        await ModifierDrag(session, 700, 400, 900, 600, button: 273);
        var resized = await Geometry(session);
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
        var tiled = await Geometry(session);
        Assert.NotNull(tiled);
        Assert.Contains("tiled=Left", tiled!, StringComparison.Ordinal);

        await ModifierDrag(session, 300, 400, 400, 500);
        var moved = await Geometry(session);
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

        // press inside the client and release over the titlebar: the press routes to
        // the seat and the release would otherwise be absorbed by the frame's surface,
        // leaving the pointer in an implicit grab that never ends.
        await session.SendAsync("move 600 400");
        await session.SendAsync("button 272 1");
        await session.SendAsync("move 600 114");
        await session.SendAsync("button 272 0");
        await Task.Delay(400, TestContext.Current.CancellationToken);

        var grab = await session.WaitForAsync(
            line => line.StartsWith("GRAB ", StringComparison.Ordinal),
            poke: "where");
        Assert.NotNull(grab);
        Assert.Contains("buttons=False", grab!, StringComparison.Ordinal);

        // and a titlebar drag still works afterwards
        var before = await Geometry(session);
        await session.SendAsync("move 600 114");
        await session.SendAsync("button 272 1");
        await session.SendAsync("move 700 214");
        await session.SendAsync("button 272 0");
        await Task.Delay(500, TestContext.Current.CancellationToken);

        var after = await Geometry(session);
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

        // weston-flower carries no decoration: it answers a press by sending
        // xdg_toplevel.move, so the compositor grabs while the client still holds
        // the button. The release has to reach the client or it cannot ask twice.
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
        await Task.Delay(150, TestContext.Current.CancellationToken);
        await Drag(session, x + 100, y + height - 20, x + 100, 0);
        await session.SendAsync("key 125 0");
        await Task.Delay(150, TestContext.Current.CancellationToken);

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
        var placed = await session.WaitForAsync(
            line => line.StartsWith("WINDOW ", StringComparison.Ordinal),
            poke: "where");
        Assert.NotNull(placed);

        var x = Field(placed!, "X = ");
        var y = Field(placed!, "Y = ");
        var right = Field(placed!, "Right = ");
        var bottom = Field(placed!, "Bottom = ");

        var grabX = x - (FrameModel.BorderWidth / 2);
        var grabY = y - FrameModel.TitlebarHeight + 2;
        await Drag(session, grabX, grabY, grabX - 60, grabY - 60);

        var resized = await Geometry(session);
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
        await Task.Delay(200, TestContext.Current.CancellationToken);
        await session.SendAsync("button 272 1");
        await Task.Delay(200, TestContext.Current.CancellationToken);
        await session.SendAsync("button 272 0");
        await Task.Delay(400, TestContext.Current.CancellationToken);

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
            await Task.Delay(300, TestContext.Current.CancellationToken);
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

    private static async Task Chord(Session session, params int[] codes)
    {
        foreach (var code in codes)
        {
            await session.SendAsync($"key {code} 1");
            await Task.Delay(80, TestContext.Current.CancellationToken);
        }

        for (var i = codes.Length - 1; i >= 0; i--)
        {
            await session.SendAsync($"key {codes[i]} 0");
            await Task.Delay(80, TestContext.Current.CancellationToken);
        }

        await Task.Delay(500, TestContext.Current.CancellationToken);
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
        await session.SendAsync($"move {sx} {sy}");
        await Task.Delay(150, TestContext.Current.CancellationToken);
        await session.SendAsync("button 272 1");
        await Task.Delay(200, TestContext.Current.CancellationToken);
        await session.SendAsync($"move {ex} {ey}");
        await Task.Delay(200, TestContext.Current.CancellationToken);
        await session.SendAsync("button 272 0");
        await Task.Delay(400, TestContext.Current.CancellationToken);
    }

    private static async Task<string?> DragAndRead(Session session, int sx, int sy, int ex, int ey)
    {
        await Drag(session, sx, sy, ex, ey);
        return await Geometry(session);
    }

    private static async Task Chord(Session session, int key)
    {
        await session.SendAsync("key 125 1");
        await session.SendAsync("key 42 1");
        await session.SendAsync($"key {key} 1");
        await session.SendAsync($"key {key} 0");
        await session.SendAsync("key 42 0");
        await session.SendAsync("key 125 0");
        await Task.Delay(500, TestContext.Current.CancellationToken);
    }

    private static async Task ModifierDrag(Session session, int sx, int sy, int ex, int ey, int button = 272)
    {
        await session.SendAsync("key 125 1");
        await session.SendAsync($"move {sx} {sy}");
        await Task.Delay(150, TestContext.Current.CancellationToken);
        await session.SendAsync($"button {button} 1");
        await Task.Delay(200, TestContext.Current.CancellationToken);
        await session.SendAsync($"move {ex} {ey}");
        await Task.Delay(200, TestContext.Current.CancellationToken);
        await session.SendAsync($"button {button} 0");
        await session.SendAsync("key 125 0");
        await Task.Delay(400, TestContext.Current.CancellationToken);
    }

    private static async Task<string?> Geometry(Session session)
    {
        var line = await session.WaitForAsync(
            l => l.StartsWith("WINDOW ", StringComparison.Ordinal),
            poke: "where",
            fresh: true);
        return line;
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

        public Session(string path, string config = "false")
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
            info.ArgumentList.Add("pixman");
            info.ArgumentList.Add("--config");
            info.ArgumentList.Add(config);
            info.Environment["BASIN_TRACE"] = string.Empty;

            _process = Process.Start(info)!;
            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    lock (_gate)
                    {
                        _lines.Add(e.Data);
                    }
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

        public async Task<string?> WaitForAsync(
            Func<string, bool> predicate,
            string? poke = null,
            bool fresh = false)
        {
            var floor = 0;
            if (fresh)
            {
                lock (_gate)
                {
                    floor = _lines.Count;
                }
            }

            for (var i = 0; i < 100; i++)
            {
                if (poke is not null)
                {
                    await SendAsync(poke);
                }

                lock (_gate)
                {
                    for (var j = _lines.Count - 1; j >= floor; j--)
                    {
                        if (predicate(_lines[j]))
                        {
                            return _lines[j];
                        }
                    }
                }

                await Task.Delay(100, TestContext.Current.CancellationToken);
            }

            return null;
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
