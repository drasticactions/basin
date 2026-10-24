using System.Globalization;
using Basin.Diagnostics;
using Xunit;
using static TabWm.Tests.Prerequisites;

namespace TabWm.Tests;

public sealed class ProcessTests
{
    private const uint Tabwin = 0xFF3C7A9E;

    private static async Task<Session> StartAsync(string? home = null, params string[] extra)
    {
        var session = new Session(Compositor(), home: home, extra: extra.Length == 0 ? ["--no-xwayland", "--no-restore"] : extra);
        await session.DisplayAsync();
        Assert.NotNull(await session.WaitForPrefixAsync("LAUNCHER "));
        return session;
    }

    private static async Task<string> PlacedAsync(Session session, int mark, string? app = null)
    {
        var line = await session.WaitForAsync(l => l.StartsWith("PLACED ", StringComparison.Ordinal) && (app is null || l.Contains($"app={app}", StringComparison.Ordinal)), mark, 20_000);
        Assert.NotNull(line);
        return line!;
    }

    [Fact]
    public async Task A_launch_lands_in_its_tab_by_its_activation_token()
    {
        var foot = Program("foot");
        using var session = await StartAsync();
        var mark = session.Mark();
        await session.SendAsync($"exec {foot}");
        var launch = await session.WaitForPrefixAsync("LAUNCH tab=", mark);
        var tab = Field(launch!, "tab");
        await session.SendAsync("tab new");
        var placed = await PlacedAsync(session, mark, "foot");
        Assert.Equal("token", Field(placed, "via"));
        Assert.Equal(tab, Field(placed, "tab"));
        await session.QuitCleanlyAsync();
    }

    [Fact]
    public async Task A_child_of_a_launched_shell_lands_in_the_shell_tab_by_pid()
    {
        var tabwin = WlClient("tabwin");
        using var session = await StartAsync();
        var mark = session.Mark();
        await session.SendAsync($"exec sleep 1; {tabwin} --title kid; true");
        var launch = await session.WaitForPrefixAsync("LAUNCH tab=", mark);
        await session.SendAsync("tab new");
        var placed = await PlacedAsync(session, mark);
        Assert.Equal("pid", Field(placed, "via"));
        Assert.Equal(Field(launch!, "tab"), Field(placed, "tab"));
        var tabs = await session.TabsAsync();
        Assert.Contains(tabs, t => Field(t, "id") == Field(launch!, "tab") && Field(t, "attention") == "0");
        await session.QuitCleanlyAsync();
    }

    [Fact]
    public async Task A_window_with_a_parent_lands_in_its_parent_tab()
    {
        var tabwin = WlClient("tabwin");
        using var session = await StartAsync();
        var mark = session.Mark();
        await session.SendAsync($"exec {tabwin} --child");
        var first = await PlacedAsync(session, mark);
        var second = await session.WaitForAsync(l => l.StartsWith("PLACED ", StringComparison.Ordinal) && l.Contains("via=parent", StringComparison.Ordinal), mark);
        Assert.NotNull(second);
        Assert.Equal(Field(first, "tab"), Field(second!, "tab"));
        await session.QuitCleanlyAsync();
    }

    [Fact]
    public async Task An_untraced_window_lands_in_the_active_tab()
    {
        var tabwin = WlClient("tabwin");
        using var session = await StartAsync();
        await session.AskAsync("tab new", "TAB ");
        var tabs = await session.TabsAsync();
        var active = tabs.Single(t => Field(t, "active") == "1");
        var mark = session.Mark();
        session.StartClient(tabwin, "--title", "stray");
        var placed = await PlacedAsync(session, mark);
        Assert.Equal("active", Field(placed, "via"));
        Assert.Equal(Field(active, "id"), Field(placed, "tab"));
        await session.QuitCleanlyAsync();
    }

    [Fact]
    public async Task Switching_tabs_shows_one_tree_and_hides_the_other()
    {
        var tabwin = WlClient("tabwin");
        using var session = await StartAsync();
        var mark = session.Mark();
        await session.SendAsync($"exec {tabwin}");
        var placed = await PlacedAsync(session, mark);
        var window = (await session.WindowsAsync()).Single();
        var x = int.Parse(Field(window, "x"), CultureInfo.InvariantCulture) + 20;
        var y = int.Parse(Field(window, "y"), CultureInfo.InvariantCulture) + 20;

        var withWindow = await ShotAsync(session, "one");
        Assert.Equal(Tabwin & 0xFFFFFF, Pixel(withWindow, x, y));
        await session.SendAsync("tab new");
        var empty = await ShotAsync(session, "two");
        Assert.NotEqual(Tabwin & 0xFFFFFF, Pixel(empty, x, y));
        await session.SendAsync($"tab select {Field(placed, "tab")}");
        var back = await ShotAsync(session, "three");
        Assert.Equal(Tabwin & 0xFFFFFF, Pixel(back, x, y));
        await session.QuitCleanlyAsync();
    }

    [Fact]
    public async Task A_window_moves_to_another_tab()
    {
        var tabwin = WlClient("tabwin");
        using var session = await StartAsync();
        var mark = session.Mark();
        await session.SendAsync($"exec {tabwin}");
        var placed = await PlacedAsync(session, mark);
        var second = Field((await session.AskAsync("tab new", "TAB ")).Single(), "id");
        await session.SendAsync($"window tab {Field(placed, "window")} {second}");
        Assert.NotNull(await session.WaitForAsync(l => l == $"MOVED window={Field(placed, "window")} tab={second}", mark));
        var window = (await session.WindowsAsync()).Single();
        Assert.Equal(second, Field(window, "tab"));
        var tabs = await session.TabsAsync();
        Assert.Equal("0", Field(tabs.Single(t => Field(t, "id") == Field(placed, "tab")), "windows"));
        Assert.Equal("1", Field(tabs.Single(t => Field(t, "id") == second), "windows"));
        await session.QuitCleanlyAsync();
    }

    [Fact]
    public async Task A_polite_close_removes_the_tab_and_a_refuser_keeps_it_with_a_banner()
    {
        var tabwin = WlClient("tabwin");
        var home = Directory.CreateTempSubdirectory("tab-wm-tests-").FullName;
        var config = Path.Combine(home, "tab-wm.toml");
        File.WriteAllText(config, "[tabs]\nclose-timeout-ms = 800\n[session]\nrestore = false\n");
        try
        {
            using var session = await StartAsync(home, "--no-xwayland", "--config", config);
            var mark = session.Mark();
            await session.SendAsync($"exec {tabwin} --title polite");
            var polite = await PlacedAsync(session, mark);
            await session.SendAsync("tab new");
            mark = session.Mark();
            await session.SendAsync($"exec {tabwin} --refuse-close --title stubborn");
            var stubborn = await PlacedAsync(session, mark);

            await session.SendAsync($"tab close {Field(polite, "tab")}");
            Assert.NotNull(await session.WaitForAsync(l => l == $"CLOSED tab={Field(polite, "tab")}", mark));
            await session.SendAsync($"tab close {Field(stubborn, "tab")}");
            Assert.NotNull(await session.WaitForAsync(l => l == $"BANNER tab={Field(stubborn, "tab")} windows=1", mark));
            var tabs = await session.TabsAsync();
            var kept = tabs.Single();
            Assert.Equal(Field(stubborn, "tab"), Field(kept, "id"));
            Assert.Equal("refused", Field(kept, "closing"));
            Assert.Equal("1", Field(kept, "active"));
            await session.SendAsync($"tab keep {Field(stubborn, "tab")}");
            Assert.NotNull(await session.WaitForAsync(l => l == $"KEPT tab={Field(stubborn, "tab")}", mark));
            Assert.Equal("no", Field((await session.TabsAsync()).Single(), "closing"));
            await session.QuitCleanlyAsync();
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task A_save_dialog_during_a_polite_close_keeps_the_close_open_until_the_banner()
    {
        var tabwin = WlClient("tabwin");
        var home = Directory.CreateTempSubdirectory("tab-wm-tests-").FullName;
        var config = Path.Combine(home, "tab-wm.toml");
        File.WriteAllText(config, "[tabs]\nclose-timeout-ms = 1500\n[session]\nrestore = false\n");
        try
        {
            using var session = await StartAsync(home, "--no-xwayland", "--config", config);
            var mark = session.Mark();
            await session.SendAsync($"exec {tabwin} --ask-on-close --title editor");
            var editor = await PlacedAsync(session, mark);
            await session.AskAsync("tab new", "TAB ");
            mark = session.Mark();
            await session.SendAsync($"tab close {Field(editor, "tab")}");
            var dialog = await session.WaitForAsync(l => l.StartsWith("PLACED ", StringComparison.Ordinal) && l.Contains("via=parent", StringComparison.Ordinal), mark);
            Assert.NotNull(dialog);
            Assert.Equal(Field(editor, "tab"), Field(dialog!, "tab"));
            Assert.Equal("waiting", Field((await session.TabsAsync()).Single(t => Field(t, "id") == Field(editor, "tab")), "closing"));
            Assert.NotNull(await session.WaitForAsync(l => l == $"BANNER tab={Field(editor, "tab")} windows=2", mark));
            Assert.DoesNotContain(session.LinesSince(mark), l => l.StartsWith("KEPT ", StringComparison.Ordinal));
            await session.QuitCleanlyAsync();
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task A_tab_moves_to_the_second_output()
    {
        var tabwin = WlClient("tabwin");
        using var session = await StartAsync(null, "--no-xwayland", "--no-restore", "--outputs", "2");
        var mark = session.Mark();
        await session.SendAsync($"exec {tabwin}");
        var placed = await PlacedAsync(session, mark);
        var tabs = await session.TabsAsync();
        Assert.Equal(2, tabs.Count);
        var from = Field(tabs.Single(t => Field(t, "id") == Field(placed, "tab")), "output");
        var to = from == "0" ? "1" : "0";
        var before = int.Parse(Field((await session.WindowsAsync()).Single(), "x"), CultureInfo.InvariantCulture);
        await session.SendAsync($"tab move {Field(placed, "tab")} {to} 0");
        Assert.NotNull(await session.WaitForAsync(l => l.StartsWith($"TABMOVED tab={Field(placed, "tab")} ", StringComparison.Ordinal), mark));
        tabs = await session.TabsAsync();
        var moved = tabs.Single(t => Field(t, "id") == Field(placed, "tab"));
        Assert.Equal(to, Field(moved, "output"));
        Assert.Equal("1", Field(moved, "active"));
        Assert.Contains(tabs, t => Field(t, "output") == from);
        var expected = to == "1" ? 1280 : -1280;
        var after = before;
        for (var attempt = 0; attempt < 50 && after - before != expected; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }

            after = int.Parse(Field((await session.WindowsAsync()).Single(), "x"), CultureInfo.InvariantCulture);
        }

        Assert.Equal(expected, after - before);
        await session.QuitCleanlyAsync();
    }

    [Fact]
    public async Task An_x11_client_is_placed_by_pid_and_framed()
    {
        var xterm = Program("xterm");
        Program("Xwayland");
        using var session = await StartAsync(null, "--no-restore");
        await session.XDisplayAsync();
        var mark = session.Mark();
        await session.SendAsync($"exec {xterm}");
        var launch = await session.WaitForPrefixAsync("LAUNCH tab=", mark);
        var placed = await PlacedAsync(session, mark);
        Assert.Equal("pid", Field(placed, "via"));
        Assert.Equal(Field(launch!, "tab"), Field(placed, "tab"));
        var window = (await session.WindowsAsync()).Single();
        Assert.Equal("1", Field(window, "x11"));
        Assert.Equal("1", Field(window, "framed"));
        await session.QuitCleanlyAsync();
    }

    [Fact]
    public async Task A_session_is_saved_and_restored_across_two_runs()
    {
        var tabwin = WlClient("tabwin");
        var home = Directory.CreateTempSubdirectory("tab-wm-tests-").FullName;
        try
        {
            using (var first = await StartAsync(home, "--no-xwayland"))
            {
                var mark = first.Mark();
                await first.SendAsync($"exec {tabwin} --title one");
                await PlacedAsync(first, mark);
                var second = Field((await first.AskAsync("tab new", "TAB ")).Single(), "id");
                await first.SendAsync($"tab rename {second} Second");
                mark = first.Mark();
                await first.SendAsync($"exec {tabwin} --title two");
                await PlacedAsync(first, mark);
                await first.SendAsync($"tab mute {second} on");
                await first.QuitCleanlyAsync();
            }

            Assert.True(File.Exists(Path.Combine(home, ".local", "state", "tab-wm", "session.json")));
            using var again = new Session(Compositor(), home: home, extra: ["--no-xwayland"]);
            await again.DisplayAsync();
            Assert.NotNull(await again.WaitForPrefixAsync("SESSION restored tabs=2", timeoutMs: 20_000));
            var mark2 = 0;
            Assert.NotNull(await again.WaitForAsync(l => l.StartsWith("PLACED ", StringComparison.Ordinal), mark2, 20_000));
            var placedTwice = await again.WaitForAsync(l => l.StartsWith("PLACED ", StringComparison.Ordinal) && again.LinesSince(0).Count(x => x.StartsWith("PLACED ", StringComparison.Ordinal)) >= 2, mark2, 20_000);
            Assert.NotNull(placedTwice);
            var tabs = await again.TabsAsync();
            Assert.Equal(2, tabs.Count);
            Assert.Contains(tabs, t => t.Contains("label=\"Second\"", StringComparison.Ordinal) && Field(t, "windows") == "1" && Field(t, "active") == "1");
            Assert.Contains(tabs, t => t.Contains("label=\"one\"", StringComparison.Ordinal) && Field(t, "windows") == "1");
            await again.QuitCleanlyAsync();
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task A_corrupt_session_file_is_ignored()
    {
        var home = Directory.CreateTempSubdirectory("tab-wm-tests-").FullName;
        try
        {
            var state = Path.Combine(home, ".local", "state", "tab-wm");
            Directory.CreateDirectory(state);
            File.WriteAllText(Path.Combine(state, "session.json"), "{ not json");
            using var session = new Session(Compositor(), home: home, extra: ["--no-xwayland"]);
            await session.DisplayAsync();
            Assert.NotNull(await session.WaitForPrefixAsync("SESSION corrupt"));
            Assert.Single(await session.TabsAsync());
            await session.QuitCleanlyAsync();
            Assert.Contains("\"outputs\"", File.ReadAllText(Path.Combine(state, "session.json")), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task Super_chords_open_switch_and_close_tabs()
    {
        using var session = await StartAsync();
        Assert.Single(await session.TabsAsync());
        await ChordAsync(session, 20);
        Assert.Equal(2, (await session.TabsAsync()).Count);
        await ChordAsync(session, 2);
        Assert.Equal("1", Field((await session.TabsAsync())[0], "active"));
        await ChordAsync(session, 15);
        Assert.Equal("1", Field((await session.TabsAsync())[1], "active"));
        var mark = session.Mark();
        await ChordAsync(session, 17);
        Assert.NotNull(await session.WaitForPrefixAsync("CLOSED tab=", mark));
        Assert.Single(await session.TabsAsync());
        await ChordAsync(session, 57);
        Assert.NotNull(await session.WaitForPrefixAsync("LAUNCHER overlay shown", mark));
        await session.SendAsync("key 1 1");
        await session.SendAsync("key 1 0");
        Assert.NotNull(await session.WaitForPrefixAsync("LAUNCHER overlay hidden", mark));
        await session.QuitCleanlyAsync();
    }

    [Fact]
    public async Task The_launcher_overlay_searches_the_installed_apps()
    {
        using var session = await StartAsync();
        var mark = session.Mark();
        await session.SendAsync("launcher show");
        Assert.NotNull(await session.WaitForPrefixAsync("LAUNCHER overlay shown", mark));
        var results = await session.AskAsync("search sh", "RESULT ");
        Assert.NotEmpty(results);
        var none = await session.AskAsync("search zzzqqq", "RESULT ");
        Assert.Equal("RESULT run \"Run “zzzqqq”\"", Assert.Single(none));
        await session.SendAsync("launcher hide");
        Assert.NotNull(await session.WaitForPrefixAsync("LAUNCHER overlay hidden", mark));
        await session.QuitCleanlyAsync();
    }

    [Fact]
    public async Task A_tab_dragged_out_of_its_strip_drops_onto_the_other_output()
    {
        using var session = await StartAsync(null, "--no-xwayland", "--no-restore", "--outputs", "2");
        await session.SendAsync("move 640 360");
        var added = Field((await session.AskAsync("tab new 0", "TAB ")).Single(), "id");
        var tabs = await session.TabsAsync();
        var first = tabs.First(t => Field(t, "output") == "0" && Field(t, "index") == "0");
        var mark = session.Mark();
        foreach (var line in new[] { "move 40 20", "button 272 1", "move 45 22", "move 45 60", "move 45 100", "move 1320 20", "button 272 0" })
        {
            await session.SendAsync(line);
        }

        Assert.NotNull(await session.WaitForAsync(l => l.StartsWith($"TABDROP tab={Field(first, "id")} output=", StringComparison.Ordinal), mark));
        tabs = await session.TabsAsync();
        Assert.Equal("1", Field(tabs.Single(t => Field(t, "id") == Field(first, "id")), "output"));
        Assert.Equal("0", Field(tabs.Single(t => Field(t, "id") == added), "output"));
        await session.QuitCleanlyAsync();
    }

    [Fact]
    public async Task A_window_dropped_on_a_tab_header_moves_there_and_a_hover_switches_tabs()
    {
        var tabwin = WlClient("tabwin");
        using var session = await StartAsync();
        var mark = session.Mark();
        await session.SendAsync($"exec {tabwin} --title mover");
        var placed = await PlacedAsync(session, mark);
        var home = Field(placed, "tab");
        var second = Field((await session.AskAsync("tab new", "TAB ")).Single(), "id");
        var third = Field((await session.AskAsync("tab new", "TAB ")).Single(), "id");
        await session.AskAsync($"tab select {home}", "TAB ");
        var before = (await session.WindowsAsync()).Single();

        mark = session.Mark();
        foreach (var line in new[] { "move 640 360", "key 125 1", "button 272 1", "move 645 350", "move 170 20", "button 272 0", "key 125 0" })
        {
            await session.SendAsync(line);
        }

        Assert.NotNull(await session.WaitForAsync(l => l == $"MOVED window={Field(placed, "window")} tab={second}", mark));
        var tabs = await session.TabsAsync();
        Assert.Equal("1", Field(tabs.Single(t => Field(t, "id") == home), "active"));
        var after = (await session.WindowsAsync()).Single();
        Assert.Equal(Field(before, "x"), Field(after, "x"));
        Assert.Equal(Field(before, "y"), Field(after, "y"));

        await session.AskAsync($"tab select {second}", "TAB ");
        mark = session.Mark();
        foreach (var line in new[] { "move 640 360", "key 125 1", "button 272 1", "move 645 350", "move 280 20" })
        {
            await session.SendAsync(line);
        }

        Assert.NotNull(await session.WaitForAsync(l => l == $"MOVED window={Field(placed, "window")} tab={third}", mark));
        await session.SendAsync("button 272 0");
        await session.SendAsync("key 125 0");
        tabs = await session.TabsAsync();
        Assert.Equal("1", Field(tabs.Single(t => Field(t, "id") == third), "active"));
        Assert.Equal(third, Field((await session.WindowsAsync()).Single(), "tab"));
        await session.QuitCleanlyAsync();
    }

    [Fact]
    public async Task A_client_requested_move_works_again_after_the_first_one_ends()
    {
        var tabwin = WlClient("tabwin");
        using var session = await StartAsync();
        var mark = session.Mark();
        session.StartClient(tabwin, "--move-on-press", "--title", "dragged");
        var placed = await PlacedAsync(session, mark);

        for (var drag = 0; drag < 2; drag++)
        {
            var before = (await session.WindowsAsync()).Single();
            var x = int.Parse(Field(before, "x"), CultureInfo.InvariantCulture) + (int.Parse(Field(before, "w"), CultureInfo.InvariantCulture) / 2);
            var y = int.Parse(Field(before, "y"), CultureInfo.InvariantCulture) + (int.Parse(Field(before, "h"), CultureInfo.InvariantCulture) / 2);
            await session.SendAsync($"move {x} {y}");
            await session.SendAsync("button 272 1");
            var moved = false;
            for (var step = 1; step <= 40 && !moved; step++)
            {
                await session.SendAsync($"move {x + (step * 2)} {y}");
                moved = Field((await session.WindowsAsync()).Single(), "x") != Field(before, "x");
            }

            await session.SendAsync("button 272 0");
            Assert.True(moved, $"drag {drag + 1} of window {Field(placed, "window")} never moved it");
        }

        await session.QuitCleanlyAsync();
    }

    [Fact]
    public async Task The_settings_page_opens_saves_a_change_and_closes_and_a_dark_variant_starts_dark()
    {
        var home = Directory.CreateTempSubdirectory("tab-wm-tests-").FullName;
        var config = Path.Combine(home, "tab-wm.toml");
        File.WriteAllText(config, "[theme]\nvariant = \"dark\"\n[session]\nrestore = false\n");
        try
        {
            using var session = await StartAsync(home, "--no-xwayland", "--config", config);
            Assert.True(Pixel(await ShotAsync(session, "dark"), 640, 400) < 0x404040, "a dark variant started light");

            var mark = session.Mark();
            await session.SendAsync("settings show about");
            Assert.NotNull(await session.WaitForAsync(l => l.StartsWith("SETTINGS shown section=about", StringComparison.Ordinal), mark));
            await session.SendAsync("key 1 1");
            await session.SendAsync("key 1 0");
            Assert.NotNull(await session.WaitForAsync(l => l == "SETTINGS hidden", mark));

            await session.SendAsync("settings show general");
            Assert.NotNull(await session.WaitForAsync(l => l.StartsWith("SETTINGS shown section=general", StringComparison.Ordinal), mark));
            await session.SettleAsync();
            await ClickElementAsync(session, "CloseWhenEmpty");

            Assert.NotNull(await session.WaitForAsync(l => l == "SETTINGS saved tabs.close-when-empty", mark));
            var text = File.ReadAllText(config);
            Assert.Contains("close-when-empty = true", text, StringComparison.Ordinal);
            Assert.Contains("variant = \"dark\"", text, StringComparison.Ordinal);
            await session.QuitCleanlyAsync();
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task A_maximized_window_restores_to_where_it_was()
    {
        var tabwin = WlClient("tabwin");
        using var session = await StartAsync();
        var mark = session.Mark();
        await session.SendAsync($"exec {tabwin} --title restored");
        await PlacedAsync(session, mark);
        var before = (await session.WindowsAsync()).Single();

        await ChordAsync(session, 50);
        Assert.NotNull(await session.WaitForWindowAsync(l => Field(l, "maximized") == "1" && Field(l, "w") != Field(before, "w")));
        await ChordAsync(session, 50);
        var after = await session.WaitForWindowAsync(l => Field(l, "maximized") == "0" && Field(l, "w") == Field(before, "w"));
        Assert.NotNull(after);
        Assert.Equal(Field(before, "x"), Field(after!, "x"));
        Assert.Equal(Field(before, "y"), Field(after!, "y"));
        Assert.Equal(Field(before, "h"), Field(after!, "h"));
        await session.QuitCleanlyAsync();
    }

    [Fact]
    public async Task Super_shift_digit_moves_the_focused_window_to_that_tab()
    {
        var tabwin = WlClient("tabwin");
        using var session = await StartAsync();
        var mark = session.Mark();
        await session.SendAsync($"exec {tabwin}");
        var placed = await PlacedAsync(session, mark);
        var second = Field((await session.AskAsync("tab new", "TAB ")).Single(), "id");
        await session.AskAsync($"tab select {Field(placed, "tab")}", "TAB ");
        await session.SendAsync("key 125 1");
        await session.SendAsync("key 42 1");
        await session.SendAsync("key 3 1");
        await session.SendAsync("key 3 0");
        await session.SendAsync("key 42 0");
        await session.SendAsync("key 125 0");
        Assert.NotNull(await session.WaitForAsync(l => l == $"MOVED window={Field(placed, "window")} tab={second}", mark));
        await session.QuitCleanlyAsync();
    }

    [Fact]
    public async Task A_fullscreen_window_hides_the_strip_until_super_is_held()
    {
        var tabwin = WlClient("tabwin");
        using var session = await StartAsync();
        var mark = session.Mark();
        await session.SendAsync($"exec {tabwin} --fullscreen");
        var placed = await PlacedAsync(session, mark);
        var window = await session.WaitForWindowAsync(w => Field(w, "w") == "1280");
        Assert.NotNull(window);
        Assert.Equal("1", Field(window!, "fullscreen"));
        Assert.Equal("0", Field(window, "x"));
        Assert.Equal("0", Field(window, "y"));
        Assert.Equal("720", Field(window, "h"));
        Assert.Equal("1", Field((await session.AskAsync("tabs", "STRIP ")).Single(), "hidden"));

        await session.SendAsync("key 125 1");
        Assert.NotNull(await session.WaitForAsync(l => l.StartsWith("PEEK ", StringComparison.Ordinal) && l.EndsWith(" on", StringComparison.Ordinal), mark));
        var strip = (await session.AskAsync("tabs", "STRIP ")).Single();
        Assert.Equal("0", Field(strip, "hidden"));
        Assert.Equal("1", Field(strip, "peek"));
        await session.SendAsync("key 125 0");
        Assert.NotNull(await session.WaitForAsync(l => l.StartsWith("PEEK ", StringComparison.Ordinal) && l.EndsWith(" off", StringComparison.Ordinal), mark));
        Assert.Equal("1", Field((await session.AskAsync("tabs", "STRIP ")).Single(), "hidden"));

        await session.AskAsync("tab new", "TAB ");
        Assert.Equal("0", Field((await session.AskAsync("tabs", "STRIP ")).Single(), "hidden"));
        await session.AskAsync($"tab select {Field(placed, "tab")}", "TAB ");
        Assert.Equal("1", Field((await session.AskAsync("tabs", "STRIP ")).Single(), "hidden"));
        await session.QuitCleanlyAsync();
    }

    [Fact]
    public async Task Tabs_are_workspaces_and_activating_a_window_selects_its_tab()
    {
        var tabwin = WlClient("tabwin");
        using var session = await StartAsync();
        var mark = session.Mark();
        await session.SendAsync($"exec {tabwin} --title one");
        var placed = await PlacedAsync(session, mark);
        var second = Field((await session.AskAsync("tab new", "TAB ")).Single(), "id");
        var list = (await session.AskAsync("ipc workspaces/list {}", "OK workspaces/list ")).Single();
        Assert.Contains($"{{\"id\":{Field(placed, "tab")},\"name\":\"one\",\"active\":false", list, StringComparison.Ordinal);
        Assert.Contains($"{{\"id\":{second},\"name\":\"New Tab\",\"active\":true", list, StringComparison.Ordinal);

        var windows = (await session.AskAsync("ipc windows/list {}", "OK windows/list ")).Single();
        var id = windows[(windows.IndexOf("\"id\":", StringComparison.Ordinal) + 5)..].Split(',')[0];
        await session.AskAsync($"ipc windows/activate {{\"id\":{id}}}", "OK windows/activate");
        var tabs = await session.TabsAsync();
        Assert.Equal("1", Field(tabs.Single(t => Field(t, "id") == Field(placed, "tab")), "active"));
        Assert.Equal("1", Field((await session.WindowsAsync()).Single(), "focused"));

        await session.AskAsync($"ipc workspaces/activate {{\"id\":{second}}}", "OK workspaces/activate");
        Assert.Equal("1", Field((await session.TabsAsync()).Single(t => Field(t, "id") == second), "active"));
        await session.AskAsync($"ipc windows/send-to-workspace {{\"id\":{id},\"workspace\":{second}}}", "OK windows/send-to-workspace");
        Assert.Equal(second, Field((await session.WindowsAsync()).Single(), "tab"));
        await session.QuitCleanlyAsync();
    }

    [Fact]
    public async Task A_layer_panel_pushes_the_strip_down_and_a_session_lock_covers_it()
    {
        var panel = WlClient("panel");
        var locktest = WlClient("locktest");
        using var session = await StartAsync();
        var before = (await session.AskAsync("tabs", "STRIP ")).Single();
        Assert.StartsWith("0,", Field(before, "work").Split(',')[0] + ",", StringComparison.Ordinal);
        var mark = session.Mark();
        session.StartClient(panel, "top", "30");
        Assert.NotNull(await session.WaitForPrefixAsync("LAYER + panel", mark));
        string? strip = null;
        for (var i = 0; i < 50; i++)
        {
            strip = (await session.AskAsync("tabs", "STRIP ")).Single();
            if (Field(strip, "work") != Field(before, "work"))
            {
                break;
            }
        }

        var height = int.Parse(Field(strip!, "height"), CultureInfo.InvariantCulture);
        Assert.StartsWith($"0,{30 + height},", Field(strip!, "work"), StringComparison.Ordinal);

        session.StartClient(locktest, "2", "FFAA0000");
        Assert.NotNull(await session.WaitForAsync(l => l == "LOCK locked", mark));
        var locked = await ShotAsync(session, "locked");
        Assert.Equal(0xAA0000u, Pixel(locked, 640, 10));
        Assert.NotNull(await session.WaitForAsync(l => l == "LOCK unlocked", mark));
        await session.QuitCleanlyAsync();
    }

    [Fact]
    public async Task A_late_window_in_a_background_tab_marks_the_tab_for_attention()
    {
        var tabwin = WlClient("tabwin");
        using var session = await StartAsync();
        var mark = session.Mark();
        await session.SendAsync($"exec sleep 11; {tabwin} --title late; true");
        var launch = await session.WaitForPrefixAsync("LAUNCH tab=", mark);
        await session.AskAsync("tab new", "TAB ");
        var placed = await session.WaitForAsync(l => l.StartsWith("PLACED ", StringComparison.Ordinal), mark, 30_000);
        Assert.NotNull(placed);
        Assert.Equal(Field(launch!, "tab"), Field(placed!, "tab"));
        var tabs = await session.TabsAsync();
        var background = tabs.Single(t => Field(t, "id") == Field(launch!, "tab"));
        Assert.Equal("0", Field(background, "active"));
        Assert.Equal("1", Field(background, "attention"));
        await session.AskAsync($"tab select {Field(launch!, "tab")}", "TAB ");
        Assert.Equal("0", Field((await session.TabsAsync()).Single(t => Field(t, "id") == Field(launch!, "tab")), "attention"));
        await session.QuitCleanlyAsync();
    }

    private static async Task ChordAsync(Session session, int key)
    {
        await session.SendAsync("key 125 1");
        await session.SendAsync($"key {key} 1");
        await session.SendAsync($"key {key} 0");
        await session.SendAsync("key 125 0");
        await session.FrameCountAsync();
    }

    private static async Task<string> ShotAsync(Session session, string name)
    {
        await session.SettleAsync();
        var path = Path.Combine(session.Home, $"{name}.png");
        var mark = session.Mark();
        await session.SendAsync($"shot {path}");
        Assert.NotNull(await session.WaitForAsync(l => l == $"SHOT {path}", mark));
        return path;
    }

    private static async Task ClickElementAsync(Session session, string name)
    {
        var line = (await session.AskAsync($"element {name}", "ELEMENT ")).Single();
        var box = line.Split(' ')[2].Split(',', 'x').Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        await session.SendAsync($"move {box[0] + (box[2] / 2)} {box[1] + (box[3] / 2)}");
        await session.SendAsync("button 272 1");
        await session.SendAsync("button 272 0");
    }

    private static uint Pixel(string path, int x, int y)
    {
        var (pixels, width, _) = PngCodec.Decode(File.ReadAllBytes(path));
        var i = ((y * width) + x) * 4;
        return ((uint)pixels[i] << 16) | ((uint)pixels[i + 1] << 8) | pixels[i + 2];
    }
}
