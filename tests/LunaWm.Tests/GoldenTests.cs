using System.Runtime.CompilerServices;
using Basin.Diagnostics;
using Xunit;

namespace LunaWm.Tests;

public sealed class GoldenTests
{
    public static TheoryData<string> Renderers() =>
        new("pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite");

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

    private static void SkipUnlessRenderer(string renderer)
    {
        if (renderer is "pixman" or "skia")
        {
            return;
        }

        var node = Environment.GetEnvironmentVariable("BASIN_RENDER_NODE") ?? "/dev/dri/renderD128";
        Assert.SkipUnless(File.Exists(node), $"{renderer} needs a render node");
    }

    internal static Task<LunaSession> Start(string renderer, TempConfig config, params string[] extra) =>
        Start(renderer, config, config.Environment, extra);

    internal static async Task<LunaSession> Start(string renderer, TempConfig config, Dictionary<string, string> environment, params string[] extra)
    {
        SkipUnlessRenderer(renderer);
        var session = new LunaSession(renderer, LunaSession.Compositor(), config.Path, environment, extra);
        var chosen = await session.WaitForAsync(line => line.StartsWith("RENDERER ", StringComparison.Ordinal));
        if (chosen is null || chosen.Split(' ')[1] != renderer)
        {
            session.Dispose();
            Assert.Skip($"{renderer} fell back to {chosen}");
        }

        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));
        await session.SendAsync("clock 12:00 PM");
        return session;
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public async Task The_card_view_with_two_stacks_one_open_matches_its_golden(string renderer)
    {
        var ssdwin = WlClient("ssdwin");
        using var bus = PrivateBus.Start();
        using var system = PrivateBus.Start();
        using var config = new TempConfig();
        using var session = await Start(renderer, config, bus, system, "-c", $"{ssdwin} & sleep 1; exec {ssdwin}");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("CARD + 2 ", StringComparison.Ordinal)));
        await session.SendAsync($"launch {ssdwin}");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("CARD + 3 ", StringComparison.Ordinal)));
        await session.SendAsync("card-view");
        Assert.NotNull(await session.WaitForAsync(line => line == "CARDVIEW on"));
        await session.SendAsync("stack 2 open");
        Assert.NotNull(await session.WaitForAsync(line => line == "STACK open 0"));
        await session.SendAsync("move 5000 5000");
        await Shoot(session, "cardview", renderer);
        await session.SendAsync("quit");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("FRAMES ", StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public async Task The_status_bar_with_a_system_menu_open_matches_its_golden(string renderer)
    {
        var ssdwin = WlClient("ssdwin");
        using var bus = PrivateBus.Start();
        using var system = PrivateBus.Start();
        using var config = new TempConfig();
        using var session = await Start(renderer, config, bus, system, "-c", ssdwin);
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("CARD + 1 ", StringComparison.Ordinal)));
        await session.SendAsync("menu battery");
        Assert.NotNull(await session.WaitForAsync(line => line == "MENU battery on"));
        await session.SendAsync("move 5000 5000");
        await Shoot(session, "menu", renderer);
        await session.SendAsync("quit");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("FRAMES ", StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public async Task The_dashboard_matches_its_golden(string renderer)
    {
        using var bus = PrivateBus.Start();
        using var system = PrivateBus.Start();
        using var config = new TempConfig();
        using var session = await Start(renderer, config, bus, system);
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("NOTIFICATIONS on ", StringComparison.Ordinal)));
        foreach (var (app, summary, body) in new[] { ("Mail", "Ada Lovelace", "The engine notes are attached"), ("Mail", "Charles Babbage", "Lunch on Thursday?"), ("Calendar", "Standup", "In 10 minutes") })
        {
            var info = new System.Diagnostics.ProcessStartInfo(LunaSession.Client("notify-send")) { UseShellExecute = false };
            foreach (var argument in new[] { "-a", app, summary, body })
            {
                info.ArgumentList.Add(argument);
            }

            info.Environment["DBUS_SESSION_BUS_ADDRESS"] = bus.Address;
            using var sent = System.Diagnostics.Process.Start(info)!;
            Assert.True(sent.WaitForExit(5000));
        }

        Assert.NotNull(await session.WaitForAsync(line => line == "NOTIFY + 3 Calendar normal Standup"));
        await session.SendAsync("dashboard");
        Assert.NotNull(await session.WaitForAsync(line => line == "DASHBOARD on"));
        await session.SendAsync("move 5000 5000");
        for (var id = 1; id <= 3; id++)
        {
            await session.SendAsync("ipc lunawm/banner-end {}");
        }

        await Shoot(session, "dashboard", renderer);
        await session.SendAsync("quit");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("FRAMES ", StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public async Task The_lock_screen_matches_its_golden(string renderer)
    {
        using var bus = PrivateBus.Start();
        using var system = PrivateBus.Start();
        using var config = new TempConfig();
        using var session = await Start(renderer, config, bus, system, "--pam-helper-command", "echo 0");
        await session.SendAsync("lock");
        Assert.NotNull(await session.WaitForAsync(line => line == "LOCKED"));
        await session.SendAsync("unlock-type 1234");
        Assert.NotNull(await session.WaitForAsync(line => line == "UNLOCK pad password"));
        await session.SendAsync("move 5000 5000");
        await Shoot(session, "lock", renderer);
        await session.SendAsync("quit");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("FRAMES ", StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public async Task The_keyboard_over_a_resized_card_matches_its_golden(string renderer)
    {
        var textinput = WlClient("textinput");
        using var bus = PrivateBus.Start();
        using var system = PrivateBus.Start();
        using var config = new TempConfig(KeyboardTests.Config);
        using var session = await Start(renderer, config, bus, system, "-c", $"{textinput} email");
        Assert.NotNull(await session.WaitForAsync(line => line == "ENTER"));
        await session.SendAsync("touch 0.5 0.3");
        Assert.NotNull(await session.WaitForAsync(line => line == "INSET HEADLESS-1 340"));
        await session.SendAsync("move 5000 5000");
        await Shoot(session, "keyboard", renderer);
        await session.SendAsync("quit");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("FRAMES ", StringComparison.Ordinal)));
    }

    public static TheoryData<string, string> KeyboardScenes()
    {
        var data = new TheoryData<string, string>();
        foreach (var renderer in new[] { "pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite" })
        {
            foreach (var scene in new[] { "shiftlock", "symbol", "longpress", "xs", "l" })
            {
                data.Add(scene, renderer);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(KeyboardScenes))]
    public async Task Each_keyboard_state_matches_its_golden(string scene, string renderer)
    {
        var textinput = WlClient("textinput");
        using var bus = PrivateBus.Start();
        using var system = PrivateBus.Start();
        using var config = new TempConfig(KeyboardTests.Config);
        using var session = await Start(renderer, config, bus, system, "-c", $"{textinput} normal");
        Assert.NotNull(await session.WaitForAsync(line => line == "ENTER"));
        await session.SendAsync("touch 0.5 0.3");
        Assert.NotNull(await session.WaitForAsync(line => line == "INSET HEADLESS-1 340"));
        switch (scene)
        {
            case "shiftlock":
                await session.SendAsync("keyboard-tap shift");
                await session.SendAsync("keyboard-tap shift");
                Assert.NotNull(await session.WaitForAsync(line => line == "SHIFT lock"));
                break;
            case "symbol":
                await session.SendAsync("keyboard-tap sym");
                Assert.NotNull(await session.WaitForAsync(line => line == "  keyboard on layout=us-qwerty size=m shift=off symbol=lock field=text autocap=off last=touch", poke: "where", fresh: true));
                break;
            case "longpress":
                await session.SendAsync("keyboard-hold e 800");
                Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("LONGPRESS e,", StringComparison.Ordinal)));
                break;
            default:
                await session.SendAsync($"keyboard-size {scene}");
                Assert.NotNull(await session.WaitForAsync(line => line == $"KBSIZE {scene}"));
                break;
        }

        await session.SendAsync("move 5000 5000");
        await Shoot(session, $"keyboard-{scene}", renderer);
        await session.SendAsync("quit");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("FRAMES ", StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public async Task The_candidate_bar_from_fcitx5_matches_its_golden(string renderer)
    {
        var fcitx5 = LunaSession.Client("fcitx5");
        var textinput = WlClient("textinput");
        using var bus = PrivateBus.Start();
        using var system = PrivateBus.Start();
        using var config = new TempConfig(KeyboardTests.Config);
        var environment = KeyboardTests.Fcitx5Environment(config, bus);
        environment["DBUS_SYSTEM_BUS_ADDRESS"] = system.Address;
        environment["LUNAWM_NO_POWER"] = "1";
        using var session = await Start(renderer, config, environment, "-c", $"{fcitx5} --replace >/dev/null 2>&1 & sleep 2; exec {textinput} normal");
        Assert.NotNull(await session.WaitForAsync(line => line == "ENTER", attempts: 200));
        await session.SendAsync("touch 0.5 0.3");
        Assert.NotNull(await session.WaitForAsync(line => line == "INSET HEADLESS-1 395", attempts: 100));
        await session.SendAsync("keyboard-type hel");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("CANDIDATES ", StringComparison.Ordinal) && line.EndsWith(" first=hel", StringComparison.Ordinal)));
        await session.SendAsync("move 5000 5000");
        await Shoot(session, "keyboard-candidates", renderer);
        await session.SendAsync("quit");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("FRAMES ", StringComparison.Ordinal)));
    }

    internal static Task<LunaSession> Start(string renderer, TempConfig config, PrivateBus bus, PrivateBus system, params string[] extra)
    {
        var environment = config.Environment;
        environment["DBUS_SESSION_BUS_ADDRESS"] = bus.Address;
        environment["DBUS_SYSTEM_BUS_ADDRESS"] = system.Address;
        environment["LUNAWM_NO_POWER"] = "1";
        return Start(renderer, config, environment, extra);
    }

    internal static async Task Shoot(LunaSession session, string scene, string renderer)
    {
        await session.SettleAsync();
        var path = Path.Combine(Path.GetTempPath(), $"luna-wm-golden-{Environment.ProcessId}-{scene}-{renderer}.png");
        var mark = session.Mark();
        await session.SendAsync($"shotraw {path}");
        var reply = await session.WaitForAsync(line => line.StartsWith("SHOTRAW ", StringComparison.Ordinal), since: mark);
        Assert.True(reply is not null && reply.StartsWith($"SHOTRAW {path} ", StringComparison.Ordinal), $"[{reply}] [{path}]");
        try
        {
            AssertMatches(path, $"{scene}-{renderer}", renderer == "pixman" ? 0 : 2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AssertMatches(string actualPath, string name, int tolerance, [CallerFilePath] string sourcePath = "")
    {
        var goldenPath = Path.Combine(Path.GetDirectoryName(sourcePath)!, "Goldens", $"{name}.png");
        var actualPng = File.ReadAllBytes(actualPath);
        if (Environment.GetEnvironmentVariable("BASIN_UPDATE_GOLDENS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllBytes(goldenPath, actualPng);
            return;
        }

        if (!File.Exists(goldenPath))
        {
            throw new FileNotFoundException($"Golden '{name}' missing. Run once with BASIN_UPDATE_GOLDENS=1 and commit {goldenPath}.");
        }

        var (expected, width, height) = PngCodec.Decode(File.ReadAllBytes(goldenPath));
        var (actual, actualWidth, actualHeight) = PngCodec.Decode(actualPng);
        Assert.True(width == actualWidth && height == actualHeight, $"Golden '{name}' is {width}x{height}, the screenshot is {actualWidth}x{actualHeight}.");
        var differing = 0;
        var maxDelta = 0;
        for (var i = 0; i < expected.Length; i += 4)
        {
            var delta = 0;
            for (var channel = 0; channel < 3; channel++)
            {
                delta = Math.Max(delta, Math.Abs(expected[i + channel] - actual[i + channel]));
            }

            differing += delta > tolerance ? 1 : 0;
            maxDelta = Math.Max(maxDelta, delta);
        }

        if (differing > 0)
        {
            var kept = Path.Combine(Path.GetTempPath(), $"luna-wm-golden-{name}-actual.png");
            File.Copy(actualPath, kept, overwrite: true);
            Assert.Fail($"Golden '{name}' mismatch: {differing} pixels beyond ±{tolerance} (max channel delta {maxDelta}). Actual kept at {kept}.");
        }
    }
}
