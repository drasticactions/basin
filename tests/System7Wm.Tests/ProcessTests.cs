using System.Globalization;
using Xunit;

namespace System7Wm.Tests;

public sealed class ProcessTests
{
    private static async Task<(Session Session, string Display)> StartAsync(IReadOnlyDictionary<string, string>? environment = null, string renderer = "pixman")
    {
        var session = new Session(Prerequisites.Compositor(), renderer, environment);
        var display = await session.DisplayAsync();
        return (session, display);
    }

    private static async Task<string> WindowAsync(Session session, string app, int? since = null)
    {
        var added = await session.WaitForAsync(line => line.StartsWith("WINDOW + ", StringComparison.Ordinal) && line.Split(' ')[3] == app, since: since);
        Assert.NotNull(added);
        return added!.Split(' ')[2];
    }

    private static async Task<string?> WindowStateAsync(Session session, string id) =>
        (await session.StateAsync()).LastOrDefault(line => line.StartsWith($"WINDOW {id} ", StringComparison.Ordinal));

    [Theory]
    [InlineData("server", "Document")]
    [InlineData("none", "Document")]
    [InlineData("client", "Unframed")]
    public async Task A_toplevel_is_framed_unless_it_negotiated_client_side_decorations(string mode, string kind)
    {
        var ssdwin = Prerequisites.WlClient("ssdwin");
        var (session, display) = await StartAsync();
        using (session)
        using (var client = session.StartClient(ssdwin, display, null, mode))
        {
            var id = await WindowAsync(session, "ssdwin");
            var window = await WindowStateAsync(session, id);
            Assert.Contains($"kind={kind} ", window!, StringComparison.Ordinal);
            if (kind == "Document")
            {
                Assert.Contains(" 8,44,256x256 ", window, StringComparison.Ordinal);
            }

            await session.QuitCleanlyAsync();
        }
    }

    [Theory]
    [InlineData("kde", "dbusmenu")]
    [InlineData("gtk", "gmenu")]
    public async Task A_client_menu_shows_in_the_bar_and_activates(string protocol, string kind)
    {
        var appmenu = Prerequisites.WlClient("appmenu");
        using var bus = PrivateBus.Start();
        var environment = new Dictionary<string, string> { ["DBUS_SESSION_BUS_ADDRESS"] = bus.Address };
        var (session, display) = await StartAsync(environment);
        using (session)
        using (var client = session.StartClient(appmenu, display, environment, protocol, "60"))
        {
            Assert.NotNull(await client.WaitForAsync(line => line.StartsWith("READY ", StringComparison.Ordinal)));
            if (protocol == "gtk")
            {
                Assert.NotNull(await client.WaitForLineAsync("CAPABILITIES 2"));
            }

            var line = await session.WaitForAsync(l => l.StartsWith("APPMENU ", StringComparison.Ordinal));
            Assert.Equal(kind, line!.Split(' ')[2]);
            var bar = await session.WaitForAsync(l => l.StartsWith("MENUBAR ", StringComparison.Ordinal) && l.Contains(",File,Edit,Application", StringComparison.Ordinal) && !l.Contains("Special", StringComparison.Ordinal), poke: "where", fresh: true, attempts: 30);
            Assert.True(bar is not null, await session.StateLineAsync("MENUBAR "));
            var front = await session.StateLineAsync("FRONT ");
            Assert.EndsWith($"menus={kind}", front!, StringComparison.Ordinal);

            await session.SendAsync("menu Edit/Wrap");
            Assert.NotNull(await session.WaitForLineAsync("MENU Edit/Wrap"));
            Assert.NotNull(await client.WaitForLineAsync("ACTIVATED wrap"));
            await session.QuitCleanlyAsync();
        }
    }

    [Fact]
    public async Task A_modal_dialog_takes_activation_from_its_parent()
    {
        var ssdwin = Prerequisites.WlClient("ssdwin");
        var (session, display) = await StartAsync();
        using (session)
        using (var client = session.StartClient(ssdwin, display, null, "server", "modal"))
        {
            var parent = await WindowAsync(session, "ssdwin");
            var dialog = await session.WaitForAsync(line => line.StartsWith("WINDOW ", StringComparison.Ordinal) && line.Contains("kind=Dialog", StringComparison.Ordinal), poke: "where");
            Assert.NotNull(dialog);
            var dialogId = dialog!.Split(' ')[1];
            Assert.NotEqual(parent, dialogId);

            await session.SendAsync($"activate {parent}");
            await Task.Delay(300, TestContext.Current.CancellationToken);
            var state = await session.StateAsync();
            Assert.Contains(state, line => line.StartsWith($"WINDOW {dialogId} ", StringComparison.Ordinal) && line.Contains("active=True", StringComparison.Ordinal));
            Assert.Contains(state, line => line.StartsWith($"WINDOW {parent} ", StringComparison.Ordinal) && line.Contains("active=False", StringComparison.Ordinal));
            await session.QuitCleanlyAsync();
        }
    }

    [Fact]
    public async Task Hide_and_show_all_suspend_and_resume_the_client()
    {
        var ssdwin = Prerequisites.WlClient("ssdwin");
        var (session, display) = await StartAsync();
        using (session)
        using (var client = session.StartClient(ssdwin, display, null, "server"))
        {
            var id = await WindowAsync(session, "ssdwin");
            await session.SendAsync("hide ssdwin");
            Assert.NotNull(await session.WaitForLineAsync("HIDE ssdwin"));
            Assert.NotNull(await client.WaitForLineAsync("SUSPENDED 1"));
            Assert.Contains("hidden=True", (await WindowStateAsync(session, id))!, StringComparison.Ordinal);

            var mark = client.Mark();
            await session.SendAsync("show-all");
            Assert.NotNull(await session.WaitForLineAsync("SHOW ssdwin"));
            Assert.NotNull(await client.WaitForLineAsync("SUSPENDED 0", mark));
            Assert.Contains("hidden=False", (await WindowStateAsync(session, id))!, StringComparison.Ordinal);
            await session.QuitCleanlyAsync();
        }
    }

    [Fact]
    public async Task The_grow_box_delivers_one_configure_on_release()
    {
        var ssdwin = Prerequisites.WlClient("ssdwin");
        var (session, display) = await StartAsync();
        using (session)
        using (var client = session.StartClient(ssdwin, display, null, "server"))
        {
            var id = await WindowAsync(session, "ssdwin");
            await Task.Delay(500, TestContext.Current.CancellationToken);

            var mark = client.Mark();
            await session.SendAsync("move 256 292");
            await session.SendAsync("button 272 1");
            foreach (var (x, y) in new[] { (270, 300), (290, 320), (310, 340), (326, 352) })
            {
                await session.SendAsync($"move {x} {y}");
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }

            await Task.Delay(300, TestContext.Current.CancellationToken);
            Assert.Equal(0, client.Count(line => line.StartsWith("CONFIGURE ", StringComparison.Ordinal), mark));
            await session.SendAsync("button 272 0");
            var grow = await session.WaitForAsync(line => line.StartsWith($"GROW {id} ", StringComparison.Ordinal));
            Assert.NotNull(grow);
            var size = grow!.Split(' ')[2].Split('x');
            Assert.NotNull(await client.WaitForLineAsync($"CONFIGURE {size[0]} {size[1]}", mark));
            await Task.Delay(500, TestContext.Current.CancellationToken);
            Assert.Equal(1, client.Count(line => line.StartsWith("CONFIGURE ", StringComparison.Ordinal), mark));
            Assert.InRange(int.Parse(size[0], CultureInfo.InvariantCulture), 300, 340);
            await session.QuitCleanlyAsync();
        }
    }

    [Fact]
    public async Task The_bar_covers_the_output_while_a_menu_is_open()
    {
        var (session, _) = await StartAsync();
        using (session)
        {
            var rest = await session.WaitForAsync(line => line.StartsWith("MENUBAR rest ", StringComparison.Ordinal), poke: "where");
            Assert.Contains("device=1024x20", rest!, StringComparison.Ordinal);

            await session.SendAsync("move 20 10");
            await session.SendAsync("button 272 1");
            Assert.NotNull(await session.WaitForLineAsync("MENUBAR open"));
            var open = await session.StateLineAsync("MENUBAR ");
            Assert.StartsWith("MENUBAR grown open=True device=1024x768", open!, StringComparison.Ordinal);
            Assert.Equal("POPUPS 0", await session.StateLineAsync("POPUPS "));

            await session.SendAsync("button 272 0");
            Assert.NotNull(await session.WaitForLineAsync("MENUBAR closed"));
            Assert.StartsWith("MENUBAR rest open=False device=1024x20", (await session.StateLineAsync("MENUBAR "))!, StringComparison.Ordinal);
            await session.QuitCleanlyAsync();
        }
    }

    [Fact]
    public async Task Command_chords_run_menu_items()
    {
        var ssdwin = Prerequisites.WlClient("ssdwin");
        var (session, display) = await StartAsync();
        using (session)
        using (var client = session.StartClient(ssdwin, display, null, "server"))
        {
            var id = await WindowAsync(session, "ssdwin");
            await session.SendAsync("key 125 1");
            await session.SendAsync("key 17 1");
            await session.SendAsync("key 17 0");
            await session.SendAsync("key 125 0");
            Assert.NotNull(await session.WaitForLineAsync("CHORD w"));
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith($"WINDOW - {id}", StringComparison.Ordinal)));
            await session.QuitCleanlyAsync();
        }
    }

    [Fact]
    public async Task The_edit_menu_sends_its_chord_as_modifier_state_not_key_presses()
    {
        var ssdwin = Prerequisites.WlClient("ssdwin");
        var (session, display) = await StartAsync();
        using (session)
        using (var client = session.StartClient(ssdwin, display, null, "server"))
        {
            await WindowAsync(session, "ssdwin");
            await Task.Delay(300, TestContext.Current.CancellationToken);
            var mark = client.Mark();
            await session.SendAsync("menu Edit/Copy");
            Assert.NotNull(await session.WaitForLineAsync("MENU Edit/Copy"));
            Assert.NotNull(await client.WaitForLineAsync("MODIFIERS 4", mark));
            Assert.NotNull(await client.WaitForLineAsync("KEY 46 1", mark));
            Assert.NotNull(await client.WaitForLineAsync("KEY 46 0", mark));
            Assert.NotNull(await client.WaitForLineAsync("MODIFIERS 0", mark));
            Assert.Equal(0, client.Count(line => line.StartsWith("KEY 29 ", StringComparison.Ordinal), mark));
            await session.QuitCleanlyAsync();
        }
    }

    [Fact]
    public async Task A_move_the_client_asks_for_from_its_content_ends_on_the_release()
    {
        var ssdwin = Prerequisites.WlClient("ssdwin");
        var (session, display) = await StartAsync();
        using (session)
        using (var client = session.StartClient(ssdwin, display, null, "server", "move"))
        {
            var id = await WindowAsync(session, "ssdwin");
            var box = (await WindowStateAsync(session, id))!.Split(' ')[4].Split(',', 'x').Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            var (x, y) = (box[0] + 100, box[1] + 100);
            await session.SendAsync($"move {x} {y}");
            await Task.Delay(200, TestContext.Current.CancellationToken);
            await session.SendAsync("button 272 1");
            Assert.NotNull(await client.WaitForLineAsync("MOVE requested"));
            await Task.Delay(200, TestContext.Current.CancellationToken);
            await session.SendAsync($"move {x + 30} {y + 20}");
            await Task.Delay(200, TestContext.Current.CancellationToken);
            await session.SendAsync("button 272 0");

            Assert.NotNull(await session.WaitForLineAsync($"MOVE {id} {box[0] + 30} {box[1] + 20}"));
            Assert.Contains($" {box[0] + 30},{box[1] + 20},", await WindowStateAsync(session, id), StringComparison.Ordinal);
            await session.QuitCleanlyAsync();
        }
    }

    [Fact]
    public async Task The_control_panels_open_and_close()
    {
        var (session, _) = await StartAsync();
        using (session)
        {
            foreach (var name in new[] { "Desktop Patterns", "Keyboard", "Monitors", "Mouse", "Sound", "WindowShade" })
            {
                await session.SendAsync($"panel {name}");
                Assert.NotNull(await session.WaitForLineAsync($"PANEL {name} open"));
                var shell = await session.StateLineAsync("SHELL ");
                Assert.Contains($"panel:{name}", shell!, StringComparison.Ordinal);
                await session.SendAsync($"panel {name} close");
                Assert.NotNull(await session.WaitForLineAsync($"PANEL {name} closed"));
            }

            await session.SendAsync("about");
            Assert.NotNull(await session.WaitForLineAsync("ABOUT open"));
            await session.SendAsync("about close");
            Assert.NotNull(await session.WaitForLineAsync("ABOUT closed"));
            Assert.Equal("SURFACES 2", await session.StateLineAsync("SURFACES "));
            await session.QuitCleanlyAsync();
        }
    }

    private static async Task DoubleClickAsync(Session session, int x, int y)
    {
        await session.SendAsync($"move {x} {y}");
        await Task.Delay(200, TestContext.Current.CancellationToken);
        await session.SendAsync("button 272 1");
        await session.SendAsync("button 272 0");
        await Task.Delay(80, TestContext.Current.CancellationToken);
        await session.SendAsync("button 272 1");
        await session.SendAsync("button 272 0");
    }

    [Fact]
    public async Task A_finder_window_opens_folders_and_files_and_remembers_where_things_were()
    {
        var shared = Directory.CreateTempSubdirectory("system7-wm-spatial-");
        try
        {
            var tree = Path.Combine(shared.FullName, "tree");
            Directory.CreateDirectory(Path.Combine(tree, "a"));
            Directory.CreateDirectory(Path.Combine(tree, "b"));
            File.WriteAllText(Path.Combine(tree, "c.txt"), "c");
            var bin = Directory.CreateDirectory(Path.Combine(shared.FullName, "bin")).FullName;
            var opened = Path.Combine(shared.FullName, "opened");
            File.WriteAllText(Path.Combine(bin, "xdg-open"), $"#!/bin/sh\necho \"$1\" > '{opened}'\n");
            File.SetUnixFileMode(Path.Combine(bin, "xdg-open"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var environment = new Dictionary<string, string>
            {
                ["XDG_STATE_HOME"] = Path.Combine(shared.FullName, "state"),
                ["PATH"] = $"{bin}:{Environment.GetEnvironmentVariable("PATH")}",
            };

            var (session, _) = await StartAsync(environment);
            using (session)
            {
                await session.SendAsync($"finder {tree}");
                Assert.NotNull(await session.WaitForLineAsync($"FINDER {tree} open"));
                await Task.Delay(500, TestContext.Current.CancellationToken);

                await DoubleClickAsync(session, 321, 106);
                for (var i = 0; i < 50 && !File.Exists(opened); i++)
                {
                    await Task.Delay(100, TestContext.Current.CancellationToken);
                }

                Assert.Equal(Path.Combine(tree, "c.txt"), File.ReadAllText(opened).Trim());

                await session.SendAsync("move 209 106");
                await Task.Delay(200, TestContext.Current.CancellationToken);
                await session.SendAsync("button 272 1");
                foreach (var (x, y) in new[] { (220, 120), (235, 150), (250, 175), (260, 190) })
                {
                    await session.SendAsync($"move {x} {y}");
                    await Task.Delay(50, TestContext.Current.CancellationToken);
                }

                await session.SendAsync("button 272 0");
                Assert.NotNull(await session.WaitForLineAsync($"ARRANGED {tree} 1"));

                await DoubleClickAsync(session, 97, 106);
                Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SHELLWINDOW + ", StringComparison.Ordinal) && line.EndsWith($" finder:{Path.Combine(tree, "a")}", StringComparison.Ordinal)));
                await session.QuitCleanlyAsync();
            }

            (session, _) = await StartAsync(environment);
            using (session)
            {
                await session.SendAsync($"finder {tree}");
                Assert.NotNull(await session.WaitForLineAsync($"FINDER {tree} open"));
                await Task.Delay(500, TestContext.Current.CancellationToken);
                await DoubleClickAsync(session, 260, 190);
                Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SHELLWINDOW + ", StringComparison.Ordinal) && line.EndsWith($" finder:{Path.Combine(tree, "b")}", StringComparison.Ordinal)));
                await session.QuitCleanlyAsync();
            }
        }
        finally
        {
            shared.Delete(recursive: true);
        }
    }

    private static async Task<(int X, int Y, int Width)> ShellContentAsync(Session session, string key)
    {
        var line = (await session.StateAsync()).Last(l => l.StartsWith("SHELL ", StringComparison.Ordinal) && l.Split(' ')[2] == key);
        var box = line.Split(' ')[3]["device=".Length..].Split(',').Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        return (box[0], box[1], box[2]);
    }

    [Fact]
    public async Task A_rename_keeps_its_place_and_the_title_pops_up_the_enclosing_folders()
    {
        var shared = Directory.CreateTempSubdirectory("system7-wm-spatial-");
        try
        {
            var tree = Path.Combine(shared.FullName, "tree");
            Directory.CreateDirectory(Path.Combine(tree, "a"));
            Directory.CreateDirectory(Path.Combine(tree, "b"));
            var environment = new Dictionary<string, string> { ["XDG_STATE_HOME"] = Path.Combine(shared.FullName, "state") };
            var (session, _) = await StartAsync(environment);
            using (session)
            {
                await session.SendAsync($"finder {tree}");
                Assert.NotNull(await session.WaitForLineAsync($"FINDER {tree} open"));
                await Task.Delay(500, TestContext.Current.CancellationToken);
                var content = await ShellContentAsync(session, $"finder:{tree}");

                await session.SendAsync($"move {content.X + 168} {content.Y + 45}");
                await Task.Delay(200, TestContext.Current.CancellationToken);
                await session.SendAsync("button 272 1");
                await session.SendAsync("button 272 0");
                await Task.Delay(300, TestContext.Current.CancellationToken);
                await session.SendAsync("key 28 1");
                await session.SendAsync("key 28 0");
                await Task.Delay(300, TestContext.Current.CancellationToken);
                await session.SendAsync("key 44 1");
                await session.SendAsync("key 44 0");
                await session.SendAsync("key 28 1");
                await session.SendAsync("key 28 0");
                Assert.NotNull(await session.WaitForLineAsync($"RENAME {Path.Combine(tree, "z")}"));
                Assert.True(Directory.Exists(Path.Combine(tree, "z")));

                await session.SendAsync($"finder {Path.Combine(tree, "a")}");
                Assert.NotNull(await session.WaitForLineAsync($"FINDER {Path.Combine(tree, "a")} open"));
                await session.SendAsync($"finder {tree} close");
                await Task.Delay(500, TestContext.Current.CancellationToken);
                var inner = await ShellContentAsync(session, $"finder:{Path.Combine(tree, "a")}");
                var title = (X: inner.X + (inner.Width / 2), Y: inner.Y - 10);
                var mark = session.Mark();
                await session.SendAsync($"move {title.X} {title.Y}");
                await Task.Delay(200, TestContext.Current.CancellationToken);
                await session.SendAsync("key 125 1");
                await session.SendAsync("button 272 1");
                Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("PATHMENU open ", StringComparison.Ordinal), since: mark));
                await Task.Delay(400, TestContext.Current.CancellationToken);
                await session.SendAsync($"move {title.X} {title.Y + 20}");
                await Task.Delay(200, TestContext.Current.CancellationToken);
                await session.SendAsync("button 272 0");
                await session.SendAsync("key 125 0");
                Assert.NotNull(await session.WaitForLineAsync($"PATHMENU {tree}", since: mark));
                Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SHELLWINDOW + ", StringComparison.Ordinal) && line.EndsWith($" finder:{tree}", StringComparison.Ordinal), since: mark));
                await session.QuitCleanlyAsync();
            }

            var state = Directory.GetFiles(Path.Combine(shared.FullName, "state", "system7-wm", "finder"))
                .Select(File.ReadAllText).Single(text => text.Contains($"path = \"{tree}\"", StringComparison.Ordinal));
            Assert.Contains("z = [112, 0]", state, StringComparison.Ordinal);
            Assert.DoesNotContain("b = ", state, StringComparison.Ordinal);
        }
        finally
        {
            shared.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task The_beep_button_plays_the_system_seven_simple_beep()
    {
        var shared = Directory.CreateTempSubdirectory("system7-wm-beep-");
        try
        {
            var played = Path.Combine(shared.FullName, "played");
            var player = Path.Combine(shared.FullName, "pw-play");
            File.WriteAllText(player, $"#!/bin/sh\necho \"$1\" >> '{played}'\n");
            File.SetUnixFileMode(player, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var (session, _) = await StartAsync(new Dictionary<string, string> { ["PATH"] = $"{shared.FullName}:{Environment.GetEnvironmentVariable("PATH")}", ["BASIN_MUTE"] = "" });
            using (session)
            {
                await session.SendAsync("panel Sound");
                Assert.NotNull(await session.WaitForLineAsync("PANEL Sound open"));
                await session.SendAsync("ui-click panel:Sound beep");
                Assert.NotNull(await session.WaitForLineAsync("BEEP Simple Beep"));
                for (var i = 0; i < 50 && !File.Exists(played); i++)
                {
                    await Task.Delay(100, TestContext.Current.CancellationToken);
                }

                var wav = File.ReadAllText(played).Trim();
                Assert.EndsWith("alert-Simple-Beep.wav", wav, StringComparison.Ordinal);
                Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(wav), 0, 4));
                await session.QuitCleanlyAsync();
            }
        }
        finally
        {
            shared.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Without_xwayland_a_client_does_not_inherit_the_parents_display()
    {
        var shared = Directory.CreateTempSubdirectory("system7-wm-display-");
        try
        {
            var seen = Path.Combine(shared.FullName, "display");
            using (var session = new Session(Prerequisites.Compositor(), "pixman", new Dictionary<string, string> { ["DISPLAY"] = ":99" }, "--xwayland", "false"))
            {
                await session.DisplayAsync();
                await session.SendAsync($"ipc process/spawn {{\"argv\":[\"sh\",\"-c\",\"echo \\\"[$DISPLAY]\\\" > {seen}\"]}}");
                for (var i = 0; i < 50 && !File.Exists(seen); i++)
                {
                    await Task.Delay(100, TestContext.Current.CancellationToken);
                }

                Assert.Equal("[]", File.ReadAllText(seen).Trim());
                await session.QuitCleanlyAsync();
            }
        }
        finally
        {
            shared.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Empty_trash_asks_and_then_empties()
    {
        var (session, _) = await StartAsync();
        using (session)
        {
            var trash = Path.Combine(session.Home, ".local", "share", "Trash");
            Directory.CreateDirectory(Path.Combine(trash, "files"));
            Directory.CreateDirectory(Path.Combine(trash, "info"));
            File.WriteAllText(Path.Combine(trash, "files", "old.txt"), "old");
            File.WriteAllText(Path.Combine(trash, "info", "old.txt.trashinfo"), "[Trash Info]\nPath=/tmp/old.txt\nDeletionDate=2026-09-29T00:00:00\n");
            File.WriteAllText(Path.Combine(trash, "info", "ghost.txt.trashinfo"), "[Trash Info]\nPath=/tmp/ghost.txt\nDeletionDate=2026-09-29T00:00:00\n");
            File.WriteAllText(Path.Combine(trash, "files", "stray.txt"), "stray");

            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("TRASH items=1 ", StringComparison.Ordinal), poke: "trash"));
            var chose = session.Mark();
            await session.SendAsync("menu Special/Empty Trash…");
            var shown = await session.WaitForAsync(line => line.StartsWith("ALERT ", StringComparison.Ordinal) && line.EndsWith(" shown", StringComparison.Ordinal), attempts: 30);
            Assert.True(shown is not null, string.Join(" | ", session.LinesSince(chose)));
            await session.SendAsync("alert-choose OK");
            Assert.NotNull(await session.WaitForLineAsync("TRASH emptied"));
            Assert.False(File.Exists(Path.Combine(trash, "files", "old.txt")));
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(trash, "files")));
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(trash, "info")));
            await session.QuitCleanlyAsync();
        }
    }

    [Fact]
    public async Task A_layer_shell_panel_maps_and_reserves_its_edge_below_the_menu_bar()
    {
        var panel = Prerequisites.WlClient("panel");
        var (session, display) = await StartAsync();
        using (session)
        {
            using (var client = session.StartClient(panel, display, null, "top", "32"))
            {
                Assert.NotNull(await client.WaitForLineAsync("PANEL 1024x32"));
                Assert.NotNull(await session.WaitForLineAsync("LAYER + panel Top"));
                Assert.Equal("WORKAREA 0,32,1024x736", await session.StateLineAsync("WORKAREA "));
            }

            Assert.NotNull(await session.WaitForLineAsync("LAYER - panel"));
            Assert.Equal("WORKAREA 0,20,1024x748", await session.StateLineAsync("WORKAREA "));
            await session.QuitCleanlyAsync();
        }
    }

    [Fact]
    public async Task A_session_lock_takes_the_keyboard_until_it_unlocks()
    {
        var ssdwin = Prerequisites.WlClient("ssdwin");
        var script = Path.Combine(Prerequisites.RepoRoot() ?? string.Empty, "scripts", "session-lock.sh");
        Assert.SkipUnless(File.Exists(script), "scripts/session-lock.sh is missing");
        var (session, display) = await StartAsync();
        using (session)
        using (var window = session.StartClient(ssdwin, display, null, "server"))
        {
            await WindowAsync(session, "ssdwin");
            await session.SendAsync("key 30 1");
            await session.SendAsync("key 30 0");
            Assert.NotNull(await window.WaitForLineAsync("KEY 30 1"));

            using (var locker = session.StartClient(script, display, null, "2", "FF227744"))
            {
                Assert.NotNull(await session.WaitForLineAsync("LOCKED"));
                Assert.Equal("LOCK on", await session.StateLineAsync("LOCK "));
                var locked = window.Mark();
                await session.SendAsync("key 31 1");
                await session.SendAsync("key 31 0");
                Assert.NotNull(await session.WaitForLineAsync("UNLOCKED"));
                Assert.Equal(0, window.Count(line => line.StartsWith("KEY 31 ", StringComparison.Ordinal), locked));
            }

            Assert.Equal("LOCK off", await session.StateLineAsync("LOCK "));
            await session.SendAsync("key 32 1");
            await session.SendAsync("key 32 0");
            Assert.NotNull(await window.WaitForLineAsync("KEY 32 1"));
            await session.QuitCleanlyAsync();
        }
    }
}
