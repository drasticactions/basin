using Xunit;

namespace EightWm.Tests;

public sealed class KeyboardTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("eight-keyboard-");

    public void Dispose() => _dir.Delete(recursive: true);

    internal static string WlClient(string name)
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

    private string Config()
    {
        var path = Path.Combine(_dir.FullName, "eight-wm.toml");
        File.WriteAllText(path, "[start]\nscan_desktop_files = false\n");
        return path;
    }

    private Dictionary<string, string> Environment() => new()
    {
        ["BASIN_MUTE"] = "1",
        ["BASIN_STILL_CARET"] = "1",
        ["LANG"] = "en_US.UTF-8",
        ["LC_ALL"] = "en_US.UTF-8",
        ["XDG_STATE_HOME"] = Path.Combine(_dir.FullName, "state"),
        ["DBUS_SESSION_BUS_ADDRESS"] = "unix:path=/nonexistent/eight-keyboard-tests/session",
        ["DBUS_SYSTEM_BUS_ADDRESS"] = "unix:path=/nonexistent/eight-keyboard-tests/system",
    };

    internal EightSession Start(string renderer = "pixman", params string[] extra) => StartWith(string.Empty, renderer, extra);

    internal EightSession StartWith(string clientArguments, string renderer = "pixman", params string[] extra)
    {
        var client = WlClient("textinput");
        var command = clientArguments.Length > 0 ? $"{client} {clientArguments}" : client;
        return new EightSession(renderer, EightSession.Compositor(), Config(), Environment(), ["--client", command, .. extra]);
    }

    internal static async Task Touched(EightSession session)
    {
        Assert.NotNull(await session.WaitForAsync(line => line == "ENTER", attempts: 150));
        await session.SendAsync("touch 0.5 0.3");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("KEYBOARD on ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_touch_in_a_field_raises_the_keyboard_and_a_hardware_key_hides_it()
    {
        using var session = Start();
        await Touched(session);
        await session.SendAsync("keyboard-type Hi");
        Assert.NotNull(await session.WaitForAsync(line => line == "TEXT Hi"));
        var mark = session.Mark();
        await session.SendAsync("key a");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("KEYBOARD off ", StringComparison.Ordinal), since: mark));
    }

    [Fact]
    public async Task A_pointer_click_in_a_field_does_not_raise_the_keyboard_but_super_k_does()
    {
        using var session = Start();
        Assert.NotNull(await session.WaitForAsync(line => line == "ENTER", attempts: 150));
        await session.SendAsync("move 683 230");
        await session.SendAsync("click");
        Assert.Null(await session.WaitForAsync(line => line.StartsWith("KEYBOARD on ", StringComparison.Ordinal), attempts: 15));
        await session.SendAsync("key Super+k");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("KEYBOARD on ", StringComparison.Ordinal)));
        var mark = session.Mark();
        await session.SendAsync("key Super+k");
        Assert.NotNull(await session.WaitForAsync(line => line == "KBHIDE toggle", since: mark));
    }

    [Fact]
    public async Task Control_and_a_reach_the_client_as_a_key_and_not_as_text()
    {
        using var session = Start();
        await Touched(session);
        await session.SendAsync("keyboard-tap control");
        await session.SendAsync("keyboard-tap a");
        Assert.NotNull(await session.WaitForAsync(line => line == "KEY 30 1"));
        Assert.DoesNotContain(session.Lines(), line => line == "COMMIT a");
        await session.SendAsync("keyboard-tap b");
        Assert.NotNull(await session.WaitForAsync(line => line == "COMMIT b"));
    }

    [Fact]
    public async Task An_edge_swipe_that_starts_on_the_keyboard_opens_nothing()
    {
        using var session = Start();
        await Touched(session);
        var mark = session.Mark();
        await session.SendAsync("touchdrag 0.5 0.995 0 -0.3");
        Assert.Null(await session.WaitForAsync(line => line.StartsWith("EDGE ", StringComparison.Ordinal) || line.StartsWith("TITLE on", StringComparison.Ordinal), attempts: 15, since: mark));
        await session.SendAsync("keyboard");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("KEYBOARD on ", StringComparison.Ordinal), since: mark));
    }

    [Fact]
    public async Task The_shape_and_size_are_remembered_across_a_restart()
    {
        using (var session = Start())
        {
            await Touched(session);
            await session.SendAsync("keyboard-layout win8-thumb-en-us");
            Assert.NotNull(await session.WaitForAsync(line => line == "KBLAYOUT win8-thumb-en-us"));
            await session.SendAsync("keyboard-size l");
            Assert.NotNull(await session.WaitForAsync(line => line == "KBSIZE l"));
            await session.SendAsync("quit");
            Assert.True(session.WaitForExit(10_000));
        }

        Assert.Contains("win8-thumb-en-us", File.ReadAllText(Path.Combine(_dir.FullName, "state", "eight-wm", "keyboard.json")), StringComparison.Ordinal);
        using var again = Start();
        await Touched(again);
        Assert.NotNull(await again.WaitForAsync(line => line.StartsWith("KEYBOARD on ", StringComparison.Ordinal) && line.EndsWith("layout=win8-thumb-en-us size=l", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task The_app_pans_clear_of_the_caret_and_returns_when_the_keyboard_hides()
    {
        using var session = StartWith("caret=400 step=20");
        await Touched(session);
        await session.SendAsync("keyboard-type abcdefghij");
        Assert.NotNull(await session.WaitForAsync(line => line == "TEXT abcdefghij"));
        Assert.NotNull(await session.WaitForAsync(line => line == "PAN textinput 208"));
        var pans = session.Lines().Where(static line => line.StartsWith("PAN textinput ", StringComparison.Ordinal)).Select(static line => int.Parse(line.Split(' ')[2], System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(pans.Order(), pans);
        var mark = session.Mark();
        await session.SendAsync("key a");
        Assert.NotNull(await session.WaitForAsync(line => line == "PAN textinput 0", since: mark));
    }

    [Fact]
    public async Task A_caret_above_the_keyboard_is_not_panned()
    {
        using var session = Start();
        await Touched(session);
        await session.SendAsync("keyboard-type Hi");
        Assert.NotNull(await session.WaitForAsync(line => line == "TEXT Hi"));
        Assert.DoesNotContain(session.Lines(), line => line.StartsWith("PAN textinput ", StringComparison.Ordinal) && line != "PAN textinput 0");
    }

    [Theory]
    [MemberData(nameof(EightGoldens.Renderers), MemberType = typeof(EightGoldens))]
    public async Task A_panned_window_matches_its_golden(string renderer)
    {
        EightGoldens.SkipUnlessRenderer(renderer);
        using var session = StartWith("caret=500 mark", renderer);
        await EightGoldens.RequireRenderer(session, renderer);
        await Touched(session);
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("PAN textinput ", StringComparison.Ordinal) && !line.EndsWith(" 0", StringComparison.Ordinal)));
        await session.SendAsync("move 5000 5000");
        await Task.Delay(1500, TestContext.Current.CancellationToken);
        await EightGoldens.Shoot(session, "keyboard-pan", renderer);
    }

    [Theory]
    [MemberData(nameof(EightGoldens.Renderers), MemberType = typeof(EightGoldens))]
    public async Task The_three_shapes_match_their_goldens(string renderer)
    {
        EightGoldens.SkipUnlessRenderer(renderer);
        using var session = Start(renderer);
        await EightGoldens.RequireRenderer(session, renderer);
        await Touched(session);
        await session.SendAsync("move 5000 5000");
        foreach (var (id, scene) in new[] { ("win8-standard-en-us", "keyboard-standard"), ("win8-thumb-en-us", "keyboard-thumb"), ("win8-full-en-us", "keyboard-full") })
        {
            var mark = session.Mark();
            await session.SendAsync($"keyboard-layout {id}");
            if (id != "win8-standard-en-us")
            {
                Assert.NotNull(await session.WaitForAsync(line => line == $"KBLAYOUT {id}", since: mark));
            }

            await Task.Delay(800, TestContext.Current.CancellationToken);
            await EightGoldens.Shoot(session, scene, renderer);
        }
    }
}
