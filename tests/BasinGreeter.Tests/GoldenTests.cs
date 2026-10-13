using System.Runtime.CompilerServices;
using Basin.Diagnostics;
using Basin.Greetd;
using Xunit;

namespace BasinGreeter.Tests;

public sealed class GoldenTests
{
    public static TheoryData<string> Renderers() =>
        new("pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite");

    internal static async Task<GreeterSession> Start(Fixture fixture, string renderer, params string[] extra) =>
        await Start(fixture, renderer, null, extra);

    internal static async Task<GreeterSession> Start(Fixture fixture, string renderer, string? systemBus, params string[] extra)
    {
        if (renderer is not ("pixman" or "skia"))
        {
            var node = Environment.GetEnvironmentVariable("BASIN_RENDER_NODE") ?? "/dev/dri/renderD128";
            Assert.SkipUnless(File.Exists(node), $"{renderer} needs a render node");
        }

        GreeterSession session;
        if (systemBus is null)
        {
            session = fixture.Start(renderer, extra);
        }
        else
        {
            var environment = fixture.Environment;
            environment["DBUS_SYSTEM_BUS_ADDRESS"] = systemBus;
            Directory.CreateDirectory(Path.GetDirectoryName(fixture.StatePath)!);
            session = new GreeterSession(GreeterSession.Headless(renderer, fixture.ConfigPath, ["--greetd-sock", fixture.Greetd.SocketPath, "--state", fixture.StatePath, .. extra]), environment);
        }
        var chosen = await session.WaitForAsync(line => line.StartsWith("RENDERER ", StringComparison.Ordinal));
        if (chosen is null || chosen.Split(' ')[1] != renderer)
        {
            session.Dispose();
            Assert.Skip($"{renderer} fell back to {chosen}");
        }

        await session.ExpectAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal), "SOCKET");
        await session.SendAsync("clock 12:00");
        await session.SendAsync("move 5000 5000");
        return session;
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public async Task The_user_list_matches_its_golden(string renderer)
    {
        using var fixture = new Fixture();
        using var session = await Start(fixture, renderer);
        await Shoot(session, "users", renderer);
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public async Task A_secret_prompt_matches_its_golden(string renderer)
    {
        using var fixture = new Fixture();
        using var session = await Start(fixture, renderer);
        await session.SendAsync("select ada");
        await session.ExpectAsync("AUTH secret Password: ");
        await session.SendAsync("type secret");
        await Shoot(session, "secret", renderer);
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public async Task An_error_matches_its_golden(string renderer)
    {
        using var fixture = new Fixture();
        fixture.Greetd.Respond(new GreetdError(GreetdErrorType.Error, "pam_acct_mgmt: the account has expired"), new GreetdSuccess());
        using var session = await Start(fixture, renderer);
        await session.SendAsync("select charles");
        await session.ExpectAsync("AUTH secret Password: ");
        await session.SendAsync("type pw");
        await session.SendAsync("submit");
        await session.ExpectAsync("LOGIN failed error pam_acct_mgmt: the account has expired");
        await Shoot(session, "error", renderer);
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public async Task The_network_menu_asking_for_a_passphrase_matches_its_golden(string renderer)
    {
        using var system = await FakeSystem.StartAsync();
        using var fixture = new Fixture();
        using var session = await Start(fixture, renderer, system.Bus.Address);
        await session.ExpectAsync("STATUS battery=43% network=wireless:Home");
        await session.SendAsync("network open");
        await session.SendAsync("network-join Office");
        await session.ExpectAsync("NETWORK ask Office psk");
        await Shoot(session, "network", renderer);
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public async Task The_on_screen_keyboard_under_a_secret_prompt_matches_its_golden(string renderer)
    {
        using var fixture = new Fixture();
        fixture.Greetd.Respond(new GreetdAuthMessage(GreetdAuthMessageType.Secret, "Password: "));
        using var session = await Start(fixture, renderer);
        await session.SendAsync("keyboard show");
        await session.ExpectAsync("KEYBOARD on");
        await session.SendAsync("select ada");
        await session.ExpectAsync("AUTH secret Password: ");
        await Shoot(session, "keyboard", renderer);
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public async Task High_contrast_at_150_percent_matches_its_golden(string renderer)
    {
        using var fixture = new Fixture("[accessibility]\nhigh_contrast = true\ntext_scale = 1.5\n");
        fixture.Greetd.Respond(new GreetdAuthMessage(GreetdAuthMessageType.Secret, "Password: "));
        using var session = await Start(fixture, renderer);
        await session.SendAsync("select ada");
        await session.ExpectAsync("AUTH secret Password: ");
        await session.SendAsync("type secret");
        await Shoot(session, "contrast", renderer);
    }

    [Theory]
    [MemberData(nameof(Renderers))]
    public async Task Signing_in_matches_its_golden(string renderer)
    {
        using var fixture = new Fixture();
        fixture.Greetd.Respond(new GreetdAuthMessage(GreetdAuthMessageType.Secret, "Password: "), new GreetdSuccess(), new GreetdSuccess());
        fixture.Greetd.HoldStartSession();
        using var session = await Start(fixture, renderer);
        await session.SendAsync("select ada");
        await session.ExpectAsync("AUTH secret Password: ");
        await session.SendAsync("type hunter2");
        await session.SendAsync("submit");
        await session.ExpectAsync("LOGIN ok");
        await Shoot(session, "signing-in", renderer);
        fixture.Greetd.ReleaseStartSession();
        await session.ExpectAsync("START session=sway");
    }

    internal static async Task Shoot(GreeterSession session, string scene, string renderer)
    {
        await session.SettleAsync();
        var path = Path.Combine(Path.GetTempPath(), $"basin-greeter-golden-{Environment.ProcessId}-{scene}-{renderer}.png");
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
            var kept = Path.Combine(Path.GetTempPath(), $"basin-greeter-golden-{name}-actual.png");
            File.Copy(actualPath, kept, overwrite: true);
            Assert.Fail($"Golden '{name}' mismatch: {differing} pixels beyond ±{tolerance} (max channel delta {maxDelta}). Actual kept at {kept}.");
        }
    }
}
