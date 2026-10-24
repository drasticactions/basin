using Xunit;

namespace LunaWm.Tests;

public sealed class ProcessTests
{
    private static bool Starts(string line, string prefix) => line.StartsWith(prefix, StringComparison.Ordinal);

    [Fact]
    public async Task A_launched_client_becomes_a_maximized_card_under_the_status_bar()
    {
        var compositor = LunaSession.Compositor();
        var client = LunaSession.Client("weston-simple-shm");
        using var config = new TempConfig();
        using var session = new LunaSession(compositor, config.Path, config.Environment, "-c", client);
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "SOCKET ")));
        var card = await session.WaitForAsync(line => Starts(line, "CARD + 1 "));
        Assert.Contains("stack=0 size=1", card!, StringComparison.Ordinal);

        var listed = await session.WaitForAsync(line => Starts(line, "CARD 1 "), poke: "cards", fresh: true);
        Assert.Contains("x=512 y=398 scale=1 rot=0 dim=1 active", listed!, StringComparison.Ordinal);
        var output = await session.WaitForAsync(line => Starts(line, "OUTPUT HEADLESS-1 "), poke: "where", fresh: true);
        Assert.Contains("mode=maximized bar=shown title=simple-shm", output!, StringComparison.Ordinal);

        await session.SendAsync("card-view");
        Assert.NotNull(await session.WaitForAsync(line => line == "CARDVIEW on"));
        var minimized = await session.WaitForAsync(line => Starts(line, "CARD 1 "), poke: "cards", fresh: true);
        Assert.Contains("scale=0.616", minimized!, StringComparison.Ordinal);

        await session.SendAsync("card-view");
        Assert.NotNull(await session.WaitForAsync(line => line == "CARDVIEW off 1"));
        await session.SendAsync("quit");
        var frames = await session.WaitForAsync(line => Starts(line, "FRAMES "));
        Assert.NotNull(frames);
        var fields = frames!.Split(' ');
        Assert.Equal("LIVE", fields[2]);
        if (fields[3] != "untracked")
        {
            Assert.Equal("0", fields[3]);
        }
    }

    private static string WlClient(string name)
    {
        var root = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && root is not null; i++)
        {
            var candidate = Path.Combine(root, "scripts", "wlclients", "bin", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        }

        Assert.Skip($"scripts/wlclients/bin/{name} has not been built");
        return null!;
    }

    [Fact]
    public async Task A_flick_up_in_card_view_closes_the_card_and_the_client_goes()
    {
        var compositor = LunaSession.Compositor();
        var ssdwin = WlClient("ssdwin");
        using var config = new TempConfig();
        using var session = new LunaSession(compositor, config.Path, config.Environment, "-c", ssdwin);
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "CARD + 1 ")));
        await session.SendAsync("card-view");
        Assert.NotNull(await session.WaitForAsync(line => line == "CARDVIEW on"));
        await session.SettleAsync();
        await session.SendAsync("touchdrag 0.5 0.45 0 -0.4 120");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "CLOSE 1 ")));
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "FLICK 1 -")));
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "CARD - 1 ")));
        await session.SendAsync("quit");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "FRAMES ")));
    }

    [Fact]
    public async Task A_flicked_card_keeps_flying_after_its_client_has_gone()
    {
        var compositor = LunaSession.Compositor();
        var ssdwin = WlClient("ssdwin");
        using var config = new TempConfig(TempConfig.Quiet + "\n\n[cards]\ntime_scale = 10.0\n");
        using var session = new LunaSession(compositor, config.Path, config.Environment, "-c", ssdwin);
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "CARD + 1 ")));
        await session.SendAsync("card-view");
        Assert.NotNull(await session.WaitForAsync(line => line == "CARDVIEW on"));
        await session.SettleAsync();
        await session.SendAsync("touchdrag 0.5 0.45 0 -0.4 120");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "FLICK 1 -")));
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "CARD - 1 ")));
        var flying = Path.Combine(config.Directory, "flying.png");
        await session.SendAsync($"shotraw {flying}");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, $"SHOTRAW {flying} ")));
        Assert.True(ClientPixels(flying) > 1000, "the card vanished when its client closed");
        Assert.NotNull(await session.WaitForAsync(line => line == "FLOWN 1", attempts: 100));
        var flown = Path.Combine(config.Directory, "flown.png");
        await session.SendAsync($"shotraw {flown}");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, $"SHOTRAW {flown} ")));
        Assert.True(ClientPixels(flown) < 100, "the card was still drawn after its flight");
        await session.SendAsync("quit");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "FRAMES ")));
    }

    private static int ClientPixels(string path)
    {
        var (rgba, _, _) = Basin.Diagnostics.PngCodec.Decode(File.ReadAllBytes(path));
        var count = 0;
        for (var i = 0; i < rgba.Length; i += 4)
        {
            if (Math.Abs(rgba[i] - 0x3c) < 12 && Math.Abs(rgba[i + 1] - 0x7a) < 12 && Math.Abs(rgba[i + 2] - 0x9e) < 12)
            {
                count++;
            }
        }

        return count;
    }

    [Fact]
    public async Task A_slow_drag_springs_back_and_a_flick_command_below_the_threshold_keeps_the_card()
    {
        var compositor = LunaSession.Compositor();
        var ssdwin = WlClient("ssdwin");
        using var config = new TempConfig();
        using var session = new LunaSession(compositor, config.Path, config.Environment, "-c", ssdwin);
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "CARD + 1 ")));
        await session.SendAsync("card-view");
        await session.SettleAsync();
        await session.SendAsync("touchdrag 0.5 0.45 0 -0.1 1500");
        Assert.NotNull(await session.WaitForAsync(line => line == "FLICK 1 back"));
        await session.SendAsync("flick 1 -800");
        Assert.NotNull(await session.WaitForAsync(line => line == "FLICK 1 back"));
        Assert.Null(await session.WaitForAsync(line => Starts(line, "CLOSE "), attempts: 5));
        await session.SendAsync("quit");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "FRAMES ")));
    }

    [Fact]
    public async Task A_window_from_the_focused_card_joins_its_stack_and_a_shell_launch_starts_a_new_one()
    {
        var compositor = LunaSession.Compositor();
        var ssdwin = WlClient("ssdwin");
        using var config = new TempConfig();
        using var session = new LunaSession(compositor, config.Path, config.Environment, "-c", $"{ssdwin} & sleep 1; exec {ssdwin}");
        Assert.NotNull(await session.WaitForAsync(line => line == "CARD + 1 ssdwin stack=0 size=1"));
        Assert.NotNull(await session.WaitForAsync(line => line == "CARD + 2 ssdwin stack=0 size=2 joined=1"));

        await session.SendAsync($"launch {ssdwin}");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "LAUNCH ")));
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "GHOST ")));
        Assert.NotNull(await session.WaitForAsync(line => line == "CARD + 3 ssdwin stack=1 size=1"));
        var cards = await session.WaitForAsync(line => Starts(line, "VIEW HEADLESS-1 "), poke: "cards", fresh: true);
        Assert.Contains("stacks=2 active=1", cards!, StringComparison.Ordinal);
        await session.SendAsync("quit");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "FRAMES ")));
    }

    [Fact]
    public async Task The_bottom_edge_side_edges_and_keys_drive_the_cards()
    {
        var compositor = LunaSession.Compositor();
        var ssdwin = WlClient("ssdwin");
        var client = LunaSession.Client("weston-simple-shm");
        using var config = new TempConfig();
        using var session = new LunaSession(compositor, config.Path, config.Environment, "-c", $"{ssdwin} & sleep 1; exec {client}");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "CARD + 2 ")));

        await session.SendAsync("edge along -0.7");
        Assert.NotNull(await session.WaitForAsync(line => line == "SWITCH 1"));
        await session.SendAsync("key 30 1");
        await session.SendAsync("key 30 0");
        await Task.Delay(300, TestContext.Current.CancellationToken);
        await session.SendAsync("key 30 1");
        await session.SendAsync("key 30 0");
        Assert.NotNull(await session.WaitForAsync(line => line == "KEY 30 0"));
        await session.SendAsync("edge left 0.05");
        Assert.NotNull(await session.WaitForAsync(line => line == "BACK 1 XF86Back"));
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "KEY ") && line != "KEY 30 1" && line != "KEY 30 0"));

        await session.SendAsync("edge bottom 0.3");
        Assert.NotNull(await session.WaitForAsync(line => line == "CARDVIEW on"));
        await session.SendAsync("edge bottom 0.2 hold 600");
        var hold = session.Mark();
        Assert.NotNull(await session.WaitForAsync(line => line == "QUICKLAUNCH hold"));
        Assert.NotNull(await session.WaitForAsync(line => line == "EDGE end", since: hold));

        await session.SendAsync("key 125 1");
        await session.SendAsync("key 125 0");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "CARDVIEW off ")));
        await session.SendAsync("pinch 0.5 0.5 0.6 0.2");
        Assert.NotNull(await session.WaitForAsync(line => line == "PINCH end commit cardview"));
        await session.SendAsync("pinch 0.5 0.45 0.1 0.6");
        Assert.NotNull(await session.WaitForAsync(line => line == "PINCH end commit maximized"));
        await session.SendAsync("quit");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "FRAMES ")));
    }

    [Fact]
    public async Task A_mouse_drag_scrolls_the_row_and_the_wheel_steps_it()
    {
        var compositor = LunaSession.Compositor();
        var ssdwin = WlClient("ssdwin");
        using var config = new TempConfig();
        using var session = new LunaSession(compositor, config.Path, config.Environment);
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "SOCKET ")));
        await session.SendAsync($"launch {ssdwin}");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "CARD + 1 ")));
        await session.SendAsync($"launch {ssdwin}");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "CARD + 2 ")));
        await session.SendAsync("card-view");
        await session.SettleAsync();

        await session.SendAsync("move 300 400");
        await session.SendAsync("button 272 1");
        await session.SendAsync("move 500 400");
        await session.SendAsync("move 700 400");
        await session.SendAsync("button 272 0");
        Assert.NotNull(await session.WaitForAsync(line => line == "ROW 0"));
        await session.SendAsync("ipc input/axis {\"axis\":\"vertical\",\"value\":80}");
        Assert.NotNull(await session.WaitForAsync(line => line == "ROW 1"));
        await session.SettleAsync();
        await session.SendAsync("move 512 400");
        await session.SendAsync("button 272 1");
        await session.SendAsync("button 272 0");
        Assert.NotNull(await session.WaitForAsync(line => line == "CARDVIEW off 2"));
        await session.SendAsync("quit");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "FRAMES ")));
    }

    [Fact]
    public async Task An_output_size_change_resizes_every_card()
    {
        var compositor = LunaSession.Compositor();
        var ssdwin = WlClient("ssdwin");
        using var config = new TempConfig();
        using var session = new LunaSession(compositor, config.Path, config.Environment, "-c", ssdwin);
        Assert.NotNull(await session.WaitForAsync(line => line == "CONFIGURE 1024 740"));
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "CARD + 1 ")));
        await session.SendAsync("ipc outputs/apply {\"entries\":[{\"name\":\"HEADLESS-1\",\"scale\":2}]}");
        Assert.NotNull(await session.WaitForAsync(line => line == "CONFIGURE 512 356"));
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "CARD 1 ssdwin stack=0 pos=0 x=256 y=206 scale=1 "), poke: "cards", fresh: true));
        await session.SendAsync("quit");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "FRAMES ")));
    }

    [Fact]
    public async Task Super_alt_8_zooms_and_super_alt_c_draws_the_screen_curtain()
    {
        var compositor = LunaSession.Compositor();
        using var config = new TempConfig(TempConfig.Quiet + "\n\n[input.keyboard]\nlayout = \"us\"\n");
        using var session = new LunaSession(compositor, config.Path, config.Environment);
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
}
