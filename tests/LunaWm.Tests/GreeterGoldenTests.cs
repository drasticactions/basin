using Basin.Greetd;
using Xunit;

namespace LunaWm.Tests;

public sealed class GreeterGoldenTests
{
    private static async Task<LunaSession> Start(GreeterFixture fixture, string renderer)
    {
        if (renderer is not ("pixman" or "skia"))
        {
            var node = Environment.GetEnvironmentVariable("BASIN_RENDER_NODE") ?? "/dev/dri/renderD128";
            Assert.SkipUnless(File.Exists(node), $"{renderer} needs a render node");
        }

        var session = fixture.Start(renderer);
        var chosen = await session.WaitForAsync(line => line.StartsWith("RENDERER ", StringComparison.Ordinal));
        if (chosen is null || chosen.Split(' ')[1] != renderer)
        {
            session.Dispose();
            Assert.Skip($"{renderer} fell back to {chosen}");
        }

        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));
        await session.SendAsync("clock 12:00 PM");
        await session.SendAsync("move 5000 5000");
        return session;
    }

    [Theory]
    [MemberData(nameof(GoldenTests.Renderers), MemberType = typeof(GoldenTests))]
    public async Task The_user_strip_matches_its_golden(string renderer)
    {
        using var fixture = new GreeterFixture();
        using var session = await Start(fixture, renderer);
        await GoldenTests.Shoot(session, "greeter-users", renderer);
    }

    [Theory]
    [MemberData(nameof(GoldenTests.Renderers), MemberType = typeof(GoldenTests))]
    public async Task A_secret_prompt_matches_its_golden(string renderer)
    {
        using var fixture = new GreeterFixture();
        using var session = await Start(fixture, renderer);
        await session.SendAsync("select ada");
        Assert.NotNull(await session.WaitForAsync(line => line == "AUTH secret Password: "));
        await session.SendAsync("type secret");
        await GoldenTests.Shoot(session, "greeter-secret", renderer);
    }

    [Theory]
    [MemberData(nameof(GoldenTests.Renderers), MemberType = typeof(GoldenTests))]
    public async Task The_pad_above_the_keyboard_matches_its_golden(string renderer)
    {
        using var fixture = new GreeterFixture();
        using var session = await Start(fixture, renderer);
        await session.SendAsync("select ada");
        Assert.NotNull(await session.WaitForAsync(line => line == "AUTH secret Password: "));
        await session.SendAsync("keyboard show");
        Assert.NotNull(await session.WaitForAsync(line => line == "KEYBOARD on"));
        await Task.Delay(600, TestContext.Current.CancellationToken);
        await GoldenTests.Shoot(session, "greeter-keyboard", renderer);
    }

    [Theory]
    [MemberData(nameof(GoldenTests.Renderers), MemberType = typeof(GoldenTests))]
    public async Task An_error_matches_its_golden(string renderer)
    {
        using var fixture = new GreeterFixture();
        fixture.Greetd.Respond(new GreetdError(GreetdErrorType.Error, "Your account has expired"));
        using var session = await Start(fixture, renderer);
        await session.SendAsync("select charles");
        Assert.NotNull(await session.WaitForAsync(line => line == "AUTH secret Password: "));
        await session.SendAsync("answer pw");
        Assert.NotNull(await session.WaitForAsync(line => line == "LOGIN failed error Your account has expired"));
        await Task.Delay(600, TestContext.Current.CancellationToken);
        await GoldenTests.Shoot(session, "greeter-error", renderer);
    }

    [Theory]
    [MemberData(nameof(GoldenTests.Renderers), MemberType = typeof(GoldenTests))]
    public async Task Signing_in_matches_its_golden(string renderer)
    {
        using var fixture = new GreeterFixture();
        fixture.Greetd.Respond(new GreetdAuthMessage(GreetdAuthMessageType.Secret, "Password: "), new GreetdSuccess(), new GreetdSuccess());
        fixture.Greetd.HoldStartSession();
        using var session = await Start(fixture, renderer);
        await session.SendAsync("select ada");
        Assert.NotNull(await session.WaitForAsync(line => line == "AUTH secret Password: "));
        await session.SendAsync("type secret");
        await session.SendAsync("submit");
        Assert.NotNull(await session.WaitForAsync(line => line == "LOGIN ok"));
        await Task.Delay(400, TestContext.Current.CancellationToken);
        await GoldenTests.Shoot(session, "greeter-signing-in", renderer);
        fixture.Greetd.ReleaseStartSession();
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("START session=", StringComparison.Ordinal)));
    }
}
