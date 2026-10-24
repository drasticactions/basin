using System.Diagnostics;
using Xunit;

namespace DungeonWm.Tests;

public sealed class ProcessTests
{
    public static TheoryData<string> Rows => new() { "gl", "vulkan" };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task A_window_hangs_and_a_click_lands_at_the_surface_point_the_picker_chose(string row)
    {
        var (compositor, client) = Prerequisites(row, "ssdwin");
        using var session = new Session(compositor, row, client);
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));
        var mapped = await session.WaitForAsync(line => line.StartsWith("MAPPED ssdwin", StringComparison.Ordinal));
        Assert.NotNull(mapped);
        Assert.Contains("size=256x256", mapped!, StringComparison.Ordinal);
        Assert.Contains("room=1", mapped, StringComparison.Ordinal);

        await session.SendAsync("face ssdwin");
        await Use(session);
        await session.SendAsync("move 640 360");
        await session.SettleAsync();
        var pick = await session.WaitForAsync(line => line.StartsWith("PICK surface window=ssdwin at=128.0,", StringComparison.Ordinal), poke: "where", fresh: true);
        Assert.NotNull(pick);

        var y = double.Parse(pick![(pick!.IndexOf(',', StringComparison.Ordinal) + 1)..], System.Globalization.CultureInfo.InvariantCulture);

        await session.SendAsync("move 704 424");
        await session.SendAsync("button 272 true");
        await session.SendAsync("button 272 false");
        var expected = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"BUTTON 272 192.00 {y + 64:F2}");
        Assert.NotNull(await session.WaitForAsync(line => line == expected));

        await session.Quit();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task A_hangup_rereads_the_file_and_names_the_keys_that_need_a_restart(string row)
    {
        var (compositor, client) = Prerequisites(row, "ssdwin");
        var directory = Directory.CreateTempSubdirectory("dungeon-wm-reload-");
        try
        {
            var file = Path.Combine(directory.FullName, "dungeon-wm.toml");
            File.WriteAllText(file, "[world]\nrooms = 1\n\n[camera]\nfov = 70\n");
            using var session = new Session(compositor, row, client, "--config", file);
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("MAPPED ssdwin", StringComparison.Ordinal)));
            var before = await session.WaitForAsync(line => line.StartsWith("WINDOW ", StringComparison.Ordinal), poke: "where", fresh: true);
            Assert.NotNull(before);

            File.WriteAllText(file, "[world]\nrooms = 1\nseed = 99\npixels_per_meter = 500\n\n[camera]\nfov = 90\n\n[keybindings]\n\"Super+T\" = \"takeover\"\n");
            session.HangUp();
            Assert.NotNull(await session.WaitForAsync(line => line == "RELOAD bindings=1 avatar=kept restart-required=world.seed"));
            Assert.Contains("meters=0.256x0.256", before!, StringComparison.Ordinal);
            var after = await session.WaitForAsync(line => line.StartsWith("WINDOW ", StringComparison.Ordinal), poke: "where", fresh: true);
            Assert.NotNull(after);
            Assert.Contains("state=hung", after!, StringComparison.Ordinal);
            Assert.Contains("meters=0.512x0.512", after, StringComparison.Ordinal);
            await session.Quit();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task A_takeover_is_flat_at_output_size_and_returns_to_the_wall(string row)
    {
        var (compositor, client) = Prerequisites(row, "ssdwin");
        using var session = new Session(compositor, row, client);
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("MAPPED ssdwin", StringComparison.Ordinal)));

        await session.SendAsync("takeover ssdwin");
        Assert.NotNull(await session.WaitForAsync(line => line == "TAKEOVER flying ssdwin"));
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("TAKEOVER ssdwin on ", StringComparison.Ordinal)));
        Assert.NotNull(await session.WaitForAsync(
            line => line.StartsWith("WINDOW ", StringComparison.Ordinal) && line.Contains("state=takeover", StringComparison.Ordinal) && line.Contains("size=1280x720", StringComparison.Ordinal),
            poke: "where"));
        await session.SendAsync("takeover off");
        Assert.NotNull(await session.WaitForAsync(line => line == "TAKEOVER off ssdwin"));
        var window = await session.WaitForAsync(line => line.StartsWith("WINDOW ", StringComparison.Ordinal), poke: "where", fresh: true);
        Assert.Contains("state=hung", window!, StringComparison.Ordinal);

        await session.Quit();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task A_thrown_window_settles_and_a_slow_release_at_the_wall_rehangs_it(string row)
    {
        var (compositor, client) = Prerequisites(row, "ssdwin");
        using var session = new Session(compositor, row, client);
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("MAPPED ssdwin", StringComparison.Ordinal)));

        await session.SendAsync("throw ssdwin 1 2 5");
        Assert.NotNull(await session.WaitForAsync(line => line == "DETACHED ssdwin"));
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("THROW ssdwin speed=", StringComparison.Ordinal)));
        var idle = await session.WaitForAsync(line => line.StartsWith("PHYSICS awake=no", StringComparison.Ordinal) && line.Contains("bodies=1", StringComparison.Ordinal), poke: "physics-state");
        Assert.NotNull(idle);

        await session.SendAsync("hang ssdwin 1 any");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("HUNG ssdwin room=1", StringComparison.Ordinal)));

        await session.SendAsync("face ssdwin");
        await session.SendAsync("key 125 true");
        await session.SendAsync("move 640 360");
        await session.SendAsync("button 272 true");
        await session.SendAsync("walk 0 -0.6");
        Assert.NotNull(await session.WaitForAsync(line => line == "DETACHED ssdwin"));
        await Task.Delay(500, TestContext.Current.CancellationToken);
        await session.SendAsync("walk 0 0.55");
        await Task.Delay(800, TestContext.Current.CancellationToken);
        await session.SendAsync("button 272 false");
        await session.SendAsync("key 125 false");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("REHANG ssdwin", StringComparison.Ordinal)));
        var window = await session.WaitForAsync(line => line.StartsWith("WINDOW ", StringComparison.Ordinal) && line.Contains("state=hung", StringComparison.Ordinal), poke: "where");
        Assert.NotNull(window);

        await session.Quit();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task The_plaque_close_button_closes_the_window(string row)
    {
        var (compositor, client) = Prerequisites(row, "ssdwin");
        using var session = new Session(compositor, row, client);
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("MAPPED ssdwin", StringComparison.Ordinal)));
        await session.SendAsync("face ssdwin");
        await Use(session);
        var (x, y) = await Settled(session, "plaque-close");

        await session.SendAsync(Move(x, y));
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("PICK plaque window=ssdwin", StringComparison.Ordinal) && line.EndsWith(" button=close", StringComparison.Ordinal), poke: "where", fresh: true));
        await session.SendAsync("button 272 true");
        await session.SendAsync("button 272 false");
        Assert.NotNull(await session.WaitForAsync(line => line == "PLAQUE ssdwin close"));
        Assert.NotNull(await session.WaitForAsync(line => line == "UNMAPPED ssdwin"));
        Assert.NotNull(await session.WaitForAsync(line => line == "FALLING ssdwin"));
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SNAPSHOT ssdwin state=falling", StringComparison.Ordinal) && line.EndsWith("canvas=copy", StringComparison.Ordinal), poke: "where", fresh: true));
        Assert.NotNull(await session.WaitForAsync(line => line == "FADING ssdwin"));
        Assert.NotNull(await session.WaitForAsync(line => line == "FADED ssdwin"));
        await AssertNoSnapshot(session);
        await session.Quit();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task A_thrown_window_that_closes_fades_where_it_lies(string row)
    {
        var (compositor, client) = Prerequisites(row, "ssdwin");
        using var session = new Session(compositor, row, client);
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("HUNG ssdwin", StringComparison.Ordinal)));
        await session.SendAsync("throw ssdwin 0 1 3");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("PHYSICS awake=no", StringComparison.Ordinal) && line.Contains("bodies=1", StringComparison.Ordinal), poke: "physics-state"));
        await session.SendAsync("plaque ssdwin close");
        Assert.NotNull(await session.WaitForAsync(line => line == "UNMAPPED ssdwin"));
        Assert.NotNull(await session.WaitForAsync(line => line == "FADED ssdwin"));
        Assert.DoesNotContain("FALLING ssdwin", session.Lines);
        await AssertNoSnapshot(session);
        await session.Quit();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task A_window_that_closes_in_takeover_falls_off_its_slot(string row)
    {
        var (compositor, client) = Prerequisites(row, "ssdwin");
        using var session = new Session(compositor, row, client);
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("HUNG ssdwin", StringComparison.Ordinal)));
        await session.SendAsync("takeover ssdwin");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("TAKEOVER ssdwin on ", StringComparison.Ordinal)));
        await session.SendAsync("plaque ssdwin close");
        Assert.NotNull(await session.WaitForAsync(line => line == "TAKEOVER off ssdwin"));
        Assert.NotNull(await session.WaitForAsync(line => line == "FALLING ssdwin"));
        Assert.NotNull(await session.WaitForAsync(line => line == "FADED ssdwin"));
        await AssertNoSnapshot(session);
        await session.Quit();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Zero_open_and_close_times_hang_and_remove_at_once(string row)
    {
        var (compositor, client) = Prerequisites(row, "ssdwin");
        var directory = Directory.CreateTempSubdirectory("dungeon-wm-instant-");
        try
        {
            var file = Path.Combine(directory.FullName, "dungeon-wm.toml");
            File.WriteAllText(file, "[world]\nrooms = 1\n\n[windows]\nopen_ms = 0\nclose_ms = 0\n");
            using var session = new Session(compositor, row, client, "--config", file);
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("MAPPED ssdwin", StringComparison.Ordinal)));
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("WINDOW ", StringComparison.Ordinal) && line.Contains("state=hung", StringComparison.Ordinal), poke: "where"));
            await session.SendAsync("plaque ssdwin close");
            Assert.NotNull(await session.WaitForAsync(line => line == "UNMAPPED ssdwin"));
            await AssertNoSnapshot(session);
            Assert.DoesNotContain(session.Lines, line => line.StartsWith("HANGING ", StringComparison.Ordinal) || line.StartsWith("FALLING ", StringComparison.Ordinal) || line.StartsWith("FADING ", StringComparison.Ordinal));
            await session.Quit();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task A_spawn_binding_runs_its_command(string row)
    {
        var (compositor, client) = Prerequisites(row, "ssdwin");
        var directory = Directory.CreateTempSubdirectory("dungeon-wm-spawn-");
        try
        {
            var file = Path.Combine(directory.FullName, "dungeon-wm.toml");
            File.WriteAllText(file, $"[world]\nrooms = 1\n\n[keybindings]\n\"Super+Return\" = \"spawn {client}\"\n");
            using var session = new Session(compositor, row, "true", "--config", file);
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));
            await session.SendAsync("key 125 true");
            await session.SendAsync("key 28 true");
            await session.SendAsync("key 28 false");
            await session.SendAsync("key 125 false");
            Assert.NotNull(await session.WaitForAsync(line => line == $"ACTION spawn {client}"));
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SPAWN ", StringComparison.Ordinal)));
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("MAPPED ssdwin", StringComparison.Ordinal)));
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("HANGING ssdwin", StringComparison.Ordinal)));
            await session.Quit();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task A_layer_surface_that_asks_for_the_keyboard_gets_the_keys_and_w_does_not_walk(string row)
    {
        var (compositor, panel) = Prerequisites(row, "panel");
        using var session = new Session(compositor, row, $"{panel} top 40 keyboard");
        Assert.NotNull(await session.WaitForAsync(line => line == "KEYBOARD layer panel"));
        Assert.NotNull(await session.WaitForAsync(line => line == "KEYBOARD enter"));
        var before = await session.WaitForAsync(line => line.StartsWith("CAMERA ", StringComparison.Ordinal), poke: "where", fresh: true);
        Assert.NotNull(before);
        await session.SendAsync("key 17 true");
        Assert.NotNull(await session.WaitForAsync(line => line == "KEY 17 1"));
        await Task.Delay(500, TestContext.Current.CancellationToken);
        var held = await session.WaitForAsync(line => line.StartsWith("CAMERA ", StringComparison.Ordinal), poke: "where", fresh: true);
        await session.SendAsync("key 17 false");
        Assert.NotNull(await session.WaitForAsync(line => line == "KEY 17 0"));
        Assert.Equal(before![..before!.IndexOf(" yaw=", StringComparison.Ordinal)], held![..held!.IndexOf(" yaw=", StringComparison.Ordinal)]);
        await session.Quit();
    }

    private static async Task AssertNoSnapshot(Session session)
    {
        await session.SendAsync("where");
        var after = await session.WaitForAsync(line => line.StartsWith("TAKEOVER ", StringComparison.Ordinal), poke: "where", fresh: true);
        Assert.NotNull(after);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        var lines = session.Lines;
        var last = lines.Count - 1 - lines.Reverse().ToList().IndexOf(after!);
        Assert.DoesNotContain(lines.Skip(last), line => line.StartsWith("SNAPSHOT ", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task A_frame_drag_without_super_pulls_the_window_off_its_wall(string row)
    {
        var (compositor, client) = Prerequisites(row, "ssdwin");
        using var session = new Session(compositor, row, client);
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("MAPPED ssdwin", StringComparison.Ordinal)));
        await session.SendAsync("face ssdwin");
        await Use(session);
        var (x, y) = await Settled(session, "frame");

        await session.SendAsync(Move(x, y));
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("PICK frame window=ssdwin", StringComparison.Ordinal) && line.EndsWith(" part=face", StringComparison.Ordinal), poke: "where", fresh: true));
        await session.SendAsync("button 272 true");
        Assert.NotNull(await session.WaitForAsync(line => line == "PULL begin ssdwin"));
        await session.SendAsync("walk 0 -0.6");
        Assert.NotNull(await session.WaitForAsync(line => line == "DETACHED ssdwin"));
        await session.SendAsync("button 272 false");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("THROW ssdwin", StringComparison.Ordinal)));
        await session.Quit();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task A_corner_drag_resizes_the_window(string row)
    {
        var (compositor, client) = Prerequisites(row, "ssdwin");
        using var session = new Session(compositor, row, client);
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("MAPPED ssdwin", StringComparison.Ordinal)));
        await session.SendAsync("face ssdwin");
        await Use(session);
        var (x, y) = await Settled(session, "bottom-right");

        await session.SendAsync(Move(x, y));
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("PICK frame window=ssdwin", StringComparison.Ordinal) && line.EndsWith(" part=bottomright", StringComparison.Ordinal), poke: "where", fresh: true));
        await session.SendAsync("button 272 true");
        Assert.NotNull(await session.WaitForAsync(line => line == "RESIZE begin ssdwin bottomright"));
        await session.SendAsync(Move(x + 20, y + 15));
        await session.SendAsync(Move(x + 40, y + 30));
        await Task.Delay(300, TestContext.Current.CancellationToken);
        await session.SendAsync("button 272 false");
        var end = await session.WaitForAsync(line => line.StartsWith("RESIZE end ssdwin ", StringComparison.Ordinal));
        Assert.NotNull(end);
        var size = end!["RESIZE end ssdwin ".Length..].Split('x');
        Assert.True(int.Parse(size[0], System.Globalization.CultureInfo.InvariantCulture) > 256, end);
        Assert.True(int.Parse(size[1], System.Globalization.CultureInfo.InvariantCulture) > 256, end);
        await session.Quit();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task A_corner_dragged_out_and_back_returns_to_its_size(string row)
    {
        var (compositor, client) = Prerequisites(row, "ssdwin");
        using var session = new Session(compositor, row, client);
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("MAPPED ssdwin", StringComparison.Ordinal)));
        await session.SendAsync("face ssdwin");
        await Use(session);
        var (x, y) = await Settled(session, "bottom-right");

        await session.SendAsync(Move(x, y));
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("PICK frame window=ssdwin", StringComparison.Ordinal) && line.EndsWith(" part=bottomright", StringComparison.Ordinal), poke: "where", fresh: true));
        await session.SendAsync("button 272 true");
        Assert.NotNull(await session.WaitForAsync(line => line == "RESIZE begin ssdwin bottomright"));
        for (var step = 1; step <= 10; step++)
        {
            await session.SendAsync(Move(x + (step * 4), y + (step * 3)));
            await Task.Delay(60, TestContext.Current.CancellationToken);
        }

        for (var step = 9; step >= 0; step--)
        {
            await session.SendAsync(Move(x + (step * 4), y + (step * 3)));
            await Task.Delay(60, TestContext.Current.CancellationToken);
        }

        await Task.Delay(300, TestContext.Current.CancellationToken);
        await session.SendAsync("button 272 false");
        var end = await session.WaitForAsync(line => line.StartsWith("RESIZE end ssdwin ", StringComparison.Ordinal));
        Assert.NotNull(end);
        var size = end!["RESIZE end ssdwin ".Length..].Split('x');
        Assert.InRange(int.Parse(size[0], System.Globalization.CultureInfo.InvariantCulture), 248, 264);
        Assert.InRange(int.Parse(size[1], System.Globalization.CultureInfo.InvariantCulture), 248, 264);
        await session.Quit();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task A_new_window_drops_onto_its_hook_and_settles(string row)
    {
        var (compositor, client) = Prerequisites(row, "ssdwin");
        var directory = Directory.CreateTempSubdirectory("dungeon-wm-hang-");
        try
        {
            var file = Path.Combine(directory.FullName, "dungeon-wm.toml");
            File.WriteAllText(file, "[world]\nrooms = 1\n\n[windows]\nopen_ms = 3000\n");
            using var session = new Session(compositor, row, client, "--config", file);
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("MAPPED ssdwin", StringComparison.Ordinal)));
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("HANGING ssdwin room=1", StringComparison.Ordinal)));
            var dropping = await session.WaitForAsync(line => line.StartsWith("WINDOW ", StringComparison.Ordinal), poke: "where", fresh: true);
            Assert.NotNull(dropping);
            Assert.Null(session.Lines.FirstOrDefault(line => line.StartsWith("HUNG ssdwin", StringComparison.Ordinal)));
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("HUNG ssdwin room=1", StringComparison.Ordinal)));
            var hung = await session.WaitForAsync(line => line.StartsWith("WINDOW ", StringComparison.Ordinal), poke: "where", fresh: true);
            Assert.NotNull(hung);
            Assert.True(Height(dropping!) > Height(hung!) + 0.01, $"{dropping} then {hung}");
            await session.Quit();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task A_session_lock_covers_the_world_and_unlocks(string row)
    {
        var (compositor, _) = Prerequisites(row, "ssdwin");
        var script = Path.Combine(Repository(), "scripts", "session-lock.sh");
        Assert.SkipUnless(File.Exists(script), "scripts/session-lock.sh is missing");
        using var session = new Session(compositor, row, $"{script} 1 FF2080FF");
        Assert.NotNull(await session.WaitForAsync(line => line == "LOCKED"));
        var state = await session.WaitForAsync(line => line.StartsWith("TAKEOVER ", StringComparison.Ordinal), poke: "where", fresh: true);
        Assert.Contains("locked=yes", state!, StringComparison.Ordinal);
        var locked = session.Lines.ToList();
        Assert.Equal("MODE window", locked.Last(line => line.StartsWith("MODE ", StringComparison.Ordinal) && !line.Contains('=', StringComparison.Ordinal)));
        Assert.NotNull(await session.WaitForAsync(line => line == "UNLOCKED"));
        var unlocked = await session.WaitForAsync(line => line.StartsWith("MODE ", StringComparison.Ordinal) && !line.Contains('=', StringComparison.Ordinal) && session.Lines.Skip(locked.Count).Contains(line));
        Assert.Equal("MODE crosshair", unlocked);
        await session.Quit();
    }

    [Fact]
    public async Task Every_window_past_the_sixty_fourth_keeps_its_toplevel_id()
    {
        const int count = 65;
        var (compositor, client) = Prerequisites("gl", "ssdwin");
        using var session = new Session(compositor, "gl", $"for i in $(seq {count}); do {client} & done; wait");
        Assert.NotNull(await session.WaitForAsync(
            _ => session.Lines.Count(line => line.StartsWith("MAPPED ssdwin", StringComparison.Ordinal)) >= count, attempts: 600));

        var list = await session.WaitForAsync(line => line.StartsWith("OK windows/list ", StringComparison.Ordinal), poke: "ipc windows/list {}", fresh: true);
        Assert.NotNull(list);
        var ids = System.Text.RegularExpressions.Regex.Matches(list!, "\"id\":([0-9]+)")
            .Select(match => match.Groups[1].Value)
            .Where(id => id != "0")
            .Distinct()
            .ToList();
        Assert.Equal(count, ids.Count);

        foreach (var id in ids)
        {
            await session.SendAsync($"locate {id}");
        }

        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("OK windows/list ", StringComparison.Ordinal), poke: "ipc windows/list {}", fresh: true));
        Assert.DoesNotContain(session.Lines, line => line.Contains("no window", StringComparison.Ordinal));
        await session.Quit();
    }

    internal static async Task Use(Session session)
    {
        Assert.NotNull(await session.WaitForAsync(line => line == "RETICLE use", poke: "mode"));
        await session.SendAsync("use");
        Assert.NotNull(await session.WaitForAsync(line => line == "MODE window"));
    }

    private static double Height(string window)
    {
        var at = window.Split(' ').First(field => field.StartsWith("at=", StringComparison.Ordinal))["at=".Length..].Split(',');
        return double.Parse(at[1], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Move(double x, double y) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"move {x:F1} {y:F1}");

    private static async Task<(double X, double Y)> Settled(Session session, string part)
    {
        await session.SettleAsync();
        var line = await session.WaitForAsync(l => l.StartsWith($"LOCATE ssdwin {part} ", StringComparison.Ordinal), poke: "locate ssdwin", fresh: true);
        Assert.NotNull(line);

        var fields = line!.Split(' ');
        return (double.Parse(fields[3], System.Globalization.CultureInfo.InvariantCulture), double.Parse(fields[4], System.Globalization.CultureInfo.InvariantCulture));
    }

    internal static (string Compositor, string Client) Prerequisites(string row, string client)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor is Linux only");
        Assert.SkipUnless(File.Exists("/dev/dri/renderD128"), "no render node");
        var compositor = Locate("dungeon-wm");
        Assert.SkipWhen(compositor is null, "dungeon-wm has not been built beside the tests");
        var path = Path.Combine(Repository(), "scripts", "wlclients", "bin", client);
        Assert.SkipUnless(File.Exists(path), $"{client} has not been built (make -C scripts/wlclients)");
        _ = row;
        return (compositor!, path);
    }

    internal static string Repository()
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

        return AppContext.BaseDirectory;
    }

    internal static string? Locate(string name)
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

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Super_alt_8_zooms_and_super_alt_c_draws_the_screen_curtain(string row)
    {
        var (compositor, client) = Prerequisites(row, "ssdwin");
        using var session = new Session(compositor, row, client);
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));
        async Task AccessChord(int key)
        {
            foreach (var (code, pressed) in new[] { (125, "true"), (56, "true"), (key, "true"), (key, "false"), (56, "false"), (125, "false") })
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
        await session.Quit();
    }

    internal sealed class Session : IDisposable
    {
        private readonly Process _process;
        private readonly List<string> _lines = [];
        private readonly Lock _gate = new();
        private TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public const string NoPipeWire = "basin-tests-no-pipewire";

        public Session(string path, string row, string command, params string[] extra)
            : this(path, row, command, new Dictionary<string, string> { ["PIPEWIRE_REMOTE"] = NoPipeWire, ["BASIN_MUTE"] = "1" }, extra)
        {
        }

        public Session(string path, string row, string command, IReadOnlyDictionary<string, string> environment, params string[] extra)
        {
            var info = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var (name, value) in environment)
            {
                info.Environment[name] = value;
            }

            string[] config = extra.Contains("--config") ? [] : ["--config", "false"];
            string[] ipc = extra.Contains("--ipc") ? [] : ["--ipc", "false"];
            foreach (var argument in (string[])["--backend", "headless", "--renderer", row, .. config, .. ipc, "-c", command])
            {
                info.ArgumentList.Add(argument);
            }

            foreach (var argument in extra)
            {
                info.ArgumentList.Add(argument);
            }

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

        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (_gate)
                {
                    return [.. _lines];
                }
            }
        }

        public void HangUp()
        {
            using var kill = Process.Start("kill", ["-HUP", _process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            kill.WaitForExit();
        }

        public async Task SendAsync(string command)
        {
            await _process.StandardInput.WriteLineAsync(command);
            await _process.StandardInput.FlushAsync();
        }

        public async Task<string?> WaitForAsync(Func<string, bool> predicate, string? poke = null, bool fresh = false, int attempts = 150, int? since = null)
        {
            var floor = since ?? 0;
            if (fresh && since is null)
            {
                lock (_gate)
                {
                    floor = _lines.Count;
                }
            }

            var deadline = Environment.TickCount64 + (attempts * 100L);
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

        public async Task SettleAsync()
        {
            int mark;
            lock (_gate)
            {
                mark = _lines.Count;
            }

            await SendAsync("settle");
            var reply = await WaitForAsync(line => line.StartsWith("OK session/settle ", StringComparison.Ordinal) || line.StartsWith("ERR ", StringComparison.Ordinal), since: mark);
            Assert.True(reply is not null && reply.StartsWith("OK ", StringComparison.Ordinal), $"settle answered [{reply}]");
        }

        public async Task Quit()
        {
            await SendAsync("quit");
            var frames = await WaitForAsync(line => line.StartsWith("FRAMES ", StringComparison.Ordinal));
            Assert.NotNull(frames);
            var fields = frames!.Split(' ');
            Assert.Equal("LIVE", fields[2]);
            if (fields[3] != "untracked")
            {
                Assert.Equal("0", fields[3]);
            }

            Assert.True(_process.WaitForExit(10000));
            Assert.Equal(0, _process.ExitCode);
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
