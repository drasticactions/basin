using Xunit;

namespace MauiComp.Tests;

public sealed class HeadlessSessionTests
{
    [Fact]
    public async Task A_client_that_asks_for_server_decorations_maps_under_a_maui_titlebar_and_leaves_nothing_behind()
    {
        var (compositor, client) = Require();
        var ssd = RequireSsdClient();

        using var session = new Session(compositor);
        var display = await session.DisplayAsync();
        var keymap = await session.WaitForAsync(line => line.StartsWith("KEYMAP ", StringComparison.Ordinal));
        Assert.Contains("compiled=yes", keymap!, StringComparison.Ordinal);

        using var framed = Session.StartClient(ssd, display);
        var window = await session.WaitForAsync(
            line => line.StartsWith("WINDOW \"ssdwin\"", StringComparison.Ordinal),
            poke: "where");
        Assert.NotNull(window);
        Assert.Contains("focused=True", window!, StringComparison.Ordinal);
        Assert.Contains("titlebar=shown", window, StringComparison.Ordinal);
        var state = await session.StateAsync();
        Assert.Contains(state, line => line.StartsWith("SCOPES 3", StringComparison.Ordinal));

        using var bare = Session.StartClient(client, display);
        var undecorated = await session.WaitForAsync(
            line => line.StartsWith("WINDOW \"simple-shm\"", StringComparison.Ordinal),
            poke: "where");
        Assert.NotNull(undecorated);
        Assert.Contains("titlebar=none", undecorated!, StringComparison.Ordinal);
        state = await session.StateAsync();
        Assert.Contains(state, line => line.StartsWith("SCOPES 3", StringComparison.Ordinal));

        bare.Kill(entireProcessTree: true);
        framed.Kill(entireProcessTree: true);
        bare.WaitForExit(5000);
        framed.WaitForExit(5000);
        Assert.NotNull(await session.WaitForAsync(
            line => line.StartsWith("SCOPES 2", StringComparison.Ordinal), poke: "where", fresh: true));
        state = await session.StateAsync();
        Assert.DoesNotContain(state, line => line.StartsWith("WINDOW ", StringComparison.Ordinal));
        Assert.Contains(state, line => line.StartsWith("SCOPES 2", StringComparison.Ordinal));

        await AssertCleanExit(session);
    }

    [Fact]
    public async Task Super_alt_8_zooms_and_super_alt_c_draws_the_screen_curtain()
    {
        var (compositor, _) = Require();
        using var session = new Session(compositor);
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));
        async Task AccessChord(int key)
        {
            foreach (var (code, pressed) in new[] { (125, 1), (56, 1), (key, 1), (key, 0), (56, 0), (125, 0) })
            {
                await session.SendAsync($"key {code} {pressed}");
            }
        }

        await AccessChord(9);
        Assert.NotNull(await session.WaitForAsync(line => line == "ZOOM factor=2"));
        await AccessChord(46);
        Assert.NotNull(await session.WaitForAsync(line => line == "CURTAIN on"));
        await AccessChord(46);
        Assert.NotNull(await session.WaitForAsync(line => line == "CURTAIN off"));
        await AccessChord(12);
        Assert.NotNull(await session.WaitForAsync(line => line == "ZOOM factor=1.6"));
    }

    [Fact]
    public async Task A_client_that_asks_for_client_decorations_gets_none()
    {
        var (compositor, _) = Require();
        var ssd = RequireSsdClient();

        using var session = new Session(compositor);
        var display = await session.DisplayAsync();
        using var app = Session.StartClient(ssd, display, "client");
        var window = await session.WaitForAsync(line => line.StartsWith("WINDOW ", StringComparison.Ordinal), poke: "where");
        Assert.NotNull(window);
        Assert.Contains("titlebar=none", window!, StringComparison.Ordinal);

        app.Kill(entireProcessTree: true);
        app.WaitForExit(5000);
        await session.SettleAsync();
        await AssertCleanExit(session);
    }

    [Fact]
    public async Task The_start_menu_is_its_own_surface_and_programs_cascades_as_a_popup()
    {
        var (compositor, _) = Require();

        using var session = new Session(compositor);
        await session.DisplayAsync();
        var state = await session.StateAsync();
        Assert.Contains("POPUPS 0", state);
        Assert.Contains("SURFACE panel 1280x30@1", state);
        Assert.Contains("STARTMENU closed programs=closed", state);

        await session.SendAsync("launcher");
        var menu = await session.WaitForAsync(
            line => line.StartsWith("SURFACE startmenu ", StringComparison.Ordinal),
            poke: "where");
        Assert.NotNull(menu);
        Assert.Equal($"SURFACE startmenu {ShellStartMenu.Width}x{ShellStartMenu.Height}@1 at=0,{720 - 30 - ShellStartMenu.Height}", menu);
        state = await session.StateAsync();
        Assert.Contains("STARTMENU open programs=closed", state);
        Assert.Contains("SCOPES 3", state);

        await session.SendAsync("launcher programs");
        var popup = await session.WaitForAsync(
            line => line.StartsWith("SURFACE popup ", StringComparison.Ordinal),
            poke: "where");
        Assert.NotNull(popup);
        var parts = popup!.Split(' ');
        var size = parts[2].Split('@')[0].Split('x');
        var at = parts[3]["at=".Length..].Split(',');
        Assert.True(int.Parse(at[0]) >= ShellStartMenu.Width - 4, $"the programs list cascades to the right of the menu, not at {at[0]}");
        Assert.True(int.Parse(size[1]) > ShellStartMenu.Height, "the programs list is taller than the menu, so it cannot be an overlay in it");
        var bottom = int.Parse(at[1]) + int.Parse(size[1]);
        Assert.True(Math.Abs(bottom - (720 - 30)) <= 2, $"the programs list stands on the taskbar like the menu does, not with its bottom at {bottom}");
        Assert.True(int.Parse(size[1]) <= 720 - 30, "the programs list stays above the taskbar");
        state = await session.StateAsync();
        Assert.Contains("STARTMENU open programs=open", state);
        Assert.Contains("POPUPS 1", state);

        await session.SendAsync("move 640 300");
        await session.SendAsync("button 272 1");
        await session.SendAsync("button 272 0");
        var closed = await session.WaitForAsync(line => line == "STARTMENU closed programs=closed", poke: "where", fresh: true);
        Assert.NotNull(closed);
        state = await session.StateAsync();
        Assert.Contains("POPUPS 0", state);
        Assert.Contains("SCOPES 2", state);

        await session.SendAsync("move 40 705");
        await session.SendAsync("button 272 1");
        await session.SendAsync("button 272 0");
        var reopened = await session.WaitForAsync(line => line == "STARTMENU open programs=closed", poke: "where", fresh: true);
        Assert.NotNull(reopened);
        await session.SendAsync("button 272 1");
        await session.SendAsync("button 272 0");
        var toggled = await session.WaitForAsync(line => line == "STARTMENU closed programs=closed", poke: "where", fresh: true);
        Assert.NotNull(toggled);

        await AssertCleanExit(session);
    }

    [Fact]
    public async Task The_run_prompt_takes_the_keyboard_and_starts_what_is_typed()
    {
        var (compositor, _) = Require();
        var flower = Session.Which("weston-flower");
        Assert.SkipWhen(flower is null, "weston-flower is not installed");

        using var session = new Session(compositor);
        await session.DisplayAsync();

        await session.SendAsync("launcher run");
        var run = await session.WaitForAsync(line => line.StartsWith("SURFACE run ", StringComparison.Ordinal), poke: "where");
        Assert.NotNull(run);
        await session.SettleAsync();
        var state = await session.StateAsync();
        Assert.Contains(state, line => line.StartsWith("RUN open", StringComparison.Ordinal));
        Assert.Contains("STARTMENU closed programs=closed", state);
        Assert.Contains("KEYBOARD client=none ui=surface text=yes", state);

        Assert.Contains($"SURFACE run {ShellRunDialog.Width}x{ShellRunDialog.Height}@1 at=460,270", state);
        await session.SendAsync("move 500 282");
        await session.SendAsync("button 272 1");
        await session.SendAsync("move 600 382");
        var dragged = await session.WaitForAsync(
            line => line == $"SURFACE run {ShellRunDialog.Width}x{ShellRunDialog.Height}@1 at=560,370", poke: "where");
        Assert.NotNull(dragged);
        await session.SendAsync("button 272 0");
        state = await session.StateAsync();
        Assert.Contains("KEYBOARD client=none ui=surface text=yes", state);

        await session.SendAsync("key 1 1");
        await session.SendAsync("key 1 0");
        var cancelled = await session.WaitForAsync(line => line.StartsWith("RUN closed", StringComparison.Ordinal), poke: "where", fresh: true);
        Assert.NotNull(cancelled);

        await session.SendAsync("run");
        var focused = await session.WaitForAsync(
            line => line == "KEYBOARD client=none ui=surface text=yes", poke: "where", fresh: true);
        Assert.NotNull(focused);
        foreach (var key in new[] { 17, 18, 31, 20, 24, 49, 12, 33, 38, 24, 17, 18, 19 })
        {
            await session.SendAsync($"key {key} 1");
            await session.SendAsync($"key {key} 0");
        }

        await session.SendAsync("key 28 1");
        await session.SendAsync("key 28 0");

        var window = await session.WaitForAsync(
            line => line.StartsWith("WINDOW \"Flower\"", StringComparison.Ordinal),
            poke: "where");
        Assert.NotNull(window);
        Assert.Contains("focused=True", window!, StringComparison.Ordinal);
        state = await session.StateAsync();
        Assert.Contains("RUN closed text=\"weston-flower\"", state);
        Assert.Contains(state, line => line.StartsWith("KEYBOARD client=surface ui=none", StringComparison.Ordinal));

        await AssertCleanExit(session);
    }

    [Fact]
    public async Task Alt_Tab_opens_the_switcher_on_its_own_surface_and_moves_focus_on_release()
    {
        var (compositor, client) = Require();
        var flower = Session.Which("weston-flower");
        Assert.SkipWhen(flower is null, "weston-flower is not installed");

        using var session = new Session(compositor);
        var display = await session.DisplayAsync();
        using var first = Session.StartClient(client, display);
        await session.WaitForAsync(line => line.StartsWith("WINDOW \"simple-shm\"", StringComparison.Ordinal), poke: "where");
        using var second = Session.StartClient(flower!, display);
        await session.WaitForAsync(line => line.StartsWith("WINDOW \"Flower\"", StringComparison.Ordinal), poke: "where");

        var state = await session.StateAsync();
        Assert.Contains(state, line => line.StartsWith("WINDOW \"Flower\"", StringComparison.Ordinal) && line.Contains("focused=True", StringComparison.Ordinal));
        Assert.Contains("SCOPES 2", state);

        await session.SendAsync("key 56 1");
        await session.SendAsync("key 15 1");
        var open = await session.WaitForAsync(line => line == "SWITCHER open", poke: "where", fresh: true);
        Assert.NotNull(open);
        state = await session.StateAsync();
        Assert.Contains("SCOPES 3", state);

        await session.SendAsync("key 15 0");
        await session.SendAsync("key 56 0");
        var closed = await session.WaitForAsync(line => line == "SWITCHER closed", poke: "where", fresh: true);
        Assert.NotNull(closed);
        state = await session.StateAsync();
        Assert.Contains(state, line => line.StartsWith("WINDOW \"simple-shm\"", StringComparison.Ordinal) && line.Contains("focused=True", StringComparison.Ordinal));
        Assert.Contains("SCOPES 2", state);

        first.Kill(entireProcessTree: true);
        second.Kill(entireProcessTree: true);
        first.WaitForExit(5000);
        second.WaitForExit(5000);
        await session.SettleAsync();
        await AssertCleanExit(session);
    }

    [Fact]
    public async Task A_window_fades_in_when_it_maps_and_out_when_it_leaves()
    {
        var (compositor, client) = Require();

        using var session = new Session(compositor);
        var display = await session.DisplayAsync();
        var idle = await session.StateAsync();
        Assert.Contains("ANIMATING no", idle);

        using var app = Session.StartClient(client, display);
        var fading = await session.WaitForAsync(line => line == "ANIMATING yes", poke: "where");
        Assert.NotNull(fading);
        var settled = await session.WaitForAsync(line => line == "ANIMATING no", poke: "where", fresh: true);
        Assert.NotNull(settled);
        var state = await session.StateAsync();
        Assert.Contains(state, line => line.StartsWith("WINDOW ", StringComparison.Ordinal));

        app.Kill(entireProcessTree: true);
        var leaving = await session.WaitForAsync(line => line == "ANIMATING yes", poke: "where", fresh: true);
        Assert.NotNull(leaving);
        var gone = await session.WaitForAsync(line => line == "ANIMATING no", poke: "where", fresh: true);
        Assert.NotNull(gone);
        state = await session.StateAsync();
        Assert.DoesNotContain(state, line => line.StartsWith("WINDOW ", StringComparison.Ordinal));

        await AssertCleanExit(session);
    }

    [Fact]
    public async Task Minimize_squashes_a_window_into_its_task_button_and_the_button_brings_it_back()
    {
        var (compositor, _) = Require();
        var client = RequireSsdClient();

        using var session = new Session(compositor);
        var display = await session.DisplayAsync();
        using var app = Session.StartClient(client, display);
        await session.WaitForAsync(line => line.StartsWith("WINDOW ", StringComparison.Ordinal), poke: "where");
        await session.WaitForAsync(line => line == "ANIMATING no", poke: "where", fresh: true);

        await session.SendAsync("move 706 216");
        await session.SendAsync("button 272 1");
        await session.SendAsync("button 272 0");
        var squashing = await session.WaitForAsync(line => line == "ANIMATING yes", poke: "where", fresh: true);
        Assert.NotNull(squashing);
        var state = await session.StateAsync();
        Assert.Contains(state, line => line.StartsWith("WINDOW ", StringComparison.Ordinal) && line.Contains("minimized=True", StringComparison.Ordinal));
        var hidden = await session.WaitForAsync(line => line == "ANIMATING no", poke: "where", fresh: true);
        Assert.NotNull(hidden);
        await session.SendAsync("move 640 400");
        state = await session.StateAsync();
        Assert.Contains("HIT scene=none focus=none shell=yes", state);

        await session.SendAsync("move 160 705");
        await session.SendAsync("button 272 1");
        await session.SendAsync("button 272 0");
        var restoring = await session.WaitForAsync(line => line == "ANIMATING yes", poke: "where", fresh: true);
        Assert.NotNull(restoring);
        var restored = await session.WaitForAsync(line => line == "ANIMATING no", poke: "where", fresh: true);
        Assert.NotNull(restored);
        await session.SendAsync("move 640 400");
        state = await session.StateAsync();
        Assert.Contains(state, line => line.StartsWith("WINDOW ", StringComparison.Ordinal) && line.Contains("focused=True maximized=False minimized=False", StringComparison.Ordinal));
        Assert.Contains("HIT scene=surface focus=surface shell=no", state);

        app.Kill(entireProcessTree: true);
        app.WaitForExit(5000);
        await session.SettleAsync();
        await AssertCleanExit(session);
    }

    [Fact]
    public async Task A_double_click_on_the_caption_maximizes_and_restores()
    {
        var (compositor, _) = Require();
        var client = RequireSsdClient();

        using var session = new Session(compositor);
        var display = await session.DisplayAsync();
        using var app = Session.StartClient(client, display);
        await session.WaitForAsync(line => line.StartsWith("WINDOW ", StringComparison.Ordinal), poke: "where");

        await session.SendAsync("move 600 216");
        await session.SendAsync("button 272 1");
        await session.SendAsync("button 272 0");
        await session.SendAsync("button 272 1");
        await session.SendAsync("button 272 0");
        var maximized = await session.WaitForAsync(
            line => line.StartsWith("WINDOW ", StringComparison.Ordinal) && line.Contains("maximized=True", StringComparison.Ordinal) &&
                line.Contains("X = 0, Y = 25, Width = 1280, Height = 665", StringComparison.Ordinal),
            poke: "where");
        Assert.NotNull(maximized);

        await session.SettleAsync();
        await session.SendAsync("move 600 12");
        await session.SendAsync("button 272 1");
        await session.SendAsync("button 272 0");
        await session.SendAsync("button 272 1");
        await session.SendAsync("button 272 0");
        var restored = await session.WaitForAsync(
            line => line.StartsWith("WINDOW ", StringComparison.Ordinal) && line.Contains("maximized=False", StringComparison.Ordinal) &&
                line.Contains("X = 512, Y = 229, Width = 256, Height = 256", StringComparison.Ordinal),
            poke: "where");
        Assert.NotNull(restored);

        app.Kill(entireProcessTree: true);
        app.WaitForExit(5000);
        await session.SettleAsync();
        await AssertCleanExit(session);
    }

    [Fact]
    public async Task A_fullscreen_window_covers_the_output_and_hides_its_titlebar()
    {
        var (compositor, _) = Require();
        var client = RequireSsdClient();

        using var session = new Session(compositor);
        var display = await session.DisplayAsync();
        using var app = Session.StartClient(client, display, "server", "fullscreen");
        var window = await session.WaitForAsync(
            line => line.StartsWith("WINDOW ", StringComparison.Ordinal) && line.Contains("fullscreen=True", StringComparison.Ordinal) &&
                line.Contains("Width = 1280, Height = 720", StringComparison.Ordinal) && line.Contains("titlebar=hidden", StringComparison.Ordinal),
            poke: "where");
        Assert.NotNull(window);
        Assert.Contains("X = 0, Y = 0, Width = 1280, Height = 720", window!, StringComparison.Ordinal);

        await session.SendAsync("move 640 705");
        var state = await session.StateAsync();
        Assert.Contains("HIT scene=surface focus=surface shell=no", state);

        app.Kill(entireProcessTree: true);
        app.WaitForExit(5000);
        await session.SettleAsync();
        await AssertCleanExit(session);
    }

    [Fact]
    public async Task A_window_follows_a_drag_of_its_caption()
    {
        var (compositor, _) = Require();
        var client = RequireSsdClient();

        using var session = new Session(compositor);
        var display = await session.DisplayAsync();
        using var app = Session.StartClient(client, display);
        var before = await session.WaitForAsync(line => line.StartsWith("WINDOW ", StringComparison.Ordinal), poke: "where");
        Assert.NotNull(before);
        Assert.Contains("X = 512, Y = 229", before!, StringComparison.Ordinal);

        await session.SendAsync("move 600 216");
        await session.SendAsync("button 272 1");
        await session.SendAsync("move 700 316");
        await session.SendAsync("move 800 416");
        var moved = await session.WaitForAsync(
            line => line.StartsWith("WINDOW ", StringComparison.Ordinal) && line.Contains("X = 712, Y = 429", StringComparison.Ordinal),
            poke: "where");
        Assert.NotNull(moved);

        await session.SendAsync("button 272 0");
        await session.SendAsync("move 640 600");
        var state = await session.StateAsync();
        Assert.Contains(state, line => line.StartsWith("WINDOW ", StringComparison.Ordinal) && line.Contains("X = 712, Y = 429", StringComparison.Ordinal));

        app.Kill(entireProcessTree: true);
        app.WaitForExit(5000);
        await session.SettleAsync();
        await AssertCleanExit(session);
    }

    [Fact]
    public async Task Alt_F4_closes_the_focused_window()
    {
        var (compositor, client) = Require();

        using var session = new Session(compositor);
        var display = await session.DisplayAsync();
        using var app = Session.StartClient(client, display);
        await session.WaitForAsync(line => line.StartsWith("WINDOW ", StringComparison.Ordinal), poke: "where");

        await session.SendAsync("key 56 1");
        await session.SendAsync("key 62 1");
        await session.SendAsync("key 62 0");
        await session.SendAsync("key 56 0");

        var exited = await Task.Run(() => app.WaitForExit(5000), TestContext.Current.CancellationToken);
        Assert.True(exited, "the client exits when the compositor asks it to close");
        await AssertCleanExit(session);
    }

    [Fact]
    public async Task An_idle_pump_allocates_nothing()
    {
        var (compositor, _) = Require();

        using var session = new Session(compositor);
        await session.DisplayAsync();
        await session.SendAsync("clock 12:00");
        await session.SettleAsync();
        await session.SendAsync("pumps 600");
        var line = await session.WaitForAsync(l => l.StartsWith("PUMPS ", StringComparison.Ordinal));
        Assert.NotNull(line);
        Assert.Equal("PUMPS n=600 bytes=0", line);

        await AssertCleanExit(session);
    }

    [Fact]
    public async Task A_layer_shell_panel_maps_and_its_exclusive_zone_shrinks_the_work_area()
    {
        var (compositor, _) = Require();
        var panel = Session.WlClient("panel");
        Assert.SkipWhen(panel is null, "scripts/wlclients/bin/panel could not be built");

        using var session = new Session(compositor);
        var display = await session.DisplayAsync();
        var output = new List<string>();
        using (var client = Session.StartListening(panel!, display, output, "top", "32"))
        {
            Assert.True(await WaitForLineAsync(output, "PANEL 1280x32"), "the panel was never configured");
            Assert.NotNull(await session.WaitForAsync(line => line == "LAYER + panel Top"));
            var area = await session.WaitForAsync(line => line.StartsWith("AREA ", StringComparison.Ordinal), poke: "where", fresh: true);
            Assert.Contains("work=Box { X = 0, Y = 32, Width = 1280, Height = 658,", area!, StringComparison.Ordinal);
            client.Kill(entireProcessTree: true);
        }

        Assert.NotNull(await session.WaitForAsync(line => line == "LAYER - panel"));
        var restored = await session.WaitForAsync(line => line.StartsWith("AREA ", StringComparison.Ordinal), poke: "where", fresh: true);
        Assert.Contains("work=Box { X = 0, Y = 0, Width = 1280, Height = 690,", restored!, StringComparison.Ordinal);
        await AssertCleanExit(session);
    }

    [Fact]
    public async Task A_session_lock_takes_the_keyboard_from_the_focused_window_until_it_unlocks()
    {
        var (compositor, _) = Require();
        var ssd = RequireSsdClient();
        var script = Path.Combine(RepositoryRoot(), "scripts", "session-lock.sh");
        Assert.SkipUnless(File.Exists(script), "scripts/session-lock.sh is missing");

        using var session = new Session(compositor);
        var display = await session.DisplayAsync();
        var keys = new List<string>();
        using var window = Session.StartListening(ssd, display, keys, "server");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("WINDOW \"ssdwin\"", StringComparison.Ordinal) && line.Contains("focused=True", StringComparison.Ordinal), poke: "where"));
        await session.SendAsync("key 30 1");
        await session.SendAsync("key 30 0");
        Assert.True(await WaitForLineAsync(keys, "KEY 30 1"), "the focused window did not get the key before the lock");

        using (var locker = Session.StartClient(script, display, "2", "FF227744"))
        {
            Assert.NotNull(await session.WaitForAsync(line => line == "LOCKED"));
            Assert.NotNull(await session.WaitForAsync(line => line == "LOCK on", poke: "where", fresh: true));
            await session.SendAsync("key 31 1");
            await session.SendAsync("key 31 0");
            Assert.NotNull(await session.WaitForAsync(line => line == "UNLOCKED"));
            lock (keys)
            {
                Assert.DoesNotContain(keys, line => line.StartsWith("KEY 31 ", StringComparison.Ordinal));
            }
        }

        Assert.NotNull(await session.WaitForAsync(line => line == "LOCK off", poke: "where", fresh: true));
        await session.SendAsync("key 32 1");
        await session.SendAsync("key 32 0");
        Assert.True(await WaitForLineAsync(keys, "KEY 32 1"), "the keyboard did not go back to the window after the unlock");
        window.Kill(entireProcessTree: true);
        await AssertCleanExit(session);
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

    private static string RepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && directory is not null; i++)
        {
            if (File.Exists(Path.Combine(directory, "basin.slnx")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
        }

        return string.Empty;
    }

    internal static (string Compositor, string Client) Require()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor half is Linux only");
        var compositor = Session.Locate("maui-comp");
        Assert.SkipWhen(compositor is null, "maui-comp has not been built beside the tests");
        var client = Session.Which("weston-simple-shm");
        Assert.SkipWhen(client is null, "weston-simple-shm is not installed");
        return (compositor!, client!);
    }

    internal static string RequireSsdClient()
    {
        var client = Session.WlClient("ssdwin");
        Assert.SkipWhen(client is null, "scripts/wlclients/bin/ssdwin could not be built");
        return client!;
    }

    internal static async Task AssertCleanExit(Session session)
    {
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
}
