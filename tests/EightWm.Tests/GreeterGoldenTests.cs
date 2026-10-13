using Basin.Greetd;
using Xunit;

namespace EightWm.Tests;

public sealed class GreeterGoldenTests
{
    private static GreetdAuthMessage Secret => new(GreetdAuthMessageType.Secret, "Password: ");

    private static async Task<EightSession> Start(EightGreeterFixture fixture, string renderer)
    {
        EightGoldens.SkipUnlessRenderer(renderer);
        var session = fixture.Start(renderer);
        await EightGoldens.RequireRenderer(session, renderer);
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));
        Assert.NotNull(await session.WaitForAsync(line => line == "AUTH secret Password: "));
        await session.SendAsync("clock 9:41");
        await session.SendAsync("move 5000 5000");
        return session;
    }

    [Theory]
    [MemberData(nameof(EightGoldens.Renderers), MemberType = typeof(EightGoldens))]
    public async Task The_lock_curtain_matches_its_golden(string renderer)
    {
        using var fixture = new EightGreeterFixture();
        fixture.Greetd.Respond(Secret);
        using var session = await Start(fixture, renderer);
        await EightGoldens.Shoot(session, "greeter-curtain", renderer);
    }

    [Theory]
    [MemberData(nameof(EightGoldens.Renderers), MemberType = typeof(EightGoldens))]
    public async Task The_sign_in_view_matches_its_golden(string renderer)
    {
        using var fixture = new EightGreeterFixture();
        fixture.Greetd.Respond(Secret);
        using var session = await Start(fixture, renderer);
        await session.SendAsync("curtain up");
        Assert.NotNull(await session.WaitForAsync(line => line == "CURTAIN up command"));
        await session.SendAsync("type secret");
        await Task.Delay(1500, TestContext.Current.CancellationToken);
        await session.SendAsync("repaint");
        await EightGoldens.Shoot(session, "greeter-signin", renderer);
    }

    [Theory]
    [MemberData(nameof(EightGoldens.Renderers), MemberType = typeof(EightGoldens))]
    public async Task An_error_matches_its_golden(string renderer)
    {
        using var fixture = new EightGreeterFixture();
        fixture.Greetd.Respond(Secret, new GreetdError(GreetdErrorType.AuthError, "pam_authenticate: AUTH_ERR"), new GreetdSuccess(), Secret);
        using var session = await Start(fixture, renderer);
        await session.SendAsync("curtain up");
        await session.SendAsync("type wrong");
        await session.SendAsync("submit");
        Assert.NotNull(await session.WaitForAsync(line => line == "LOGIN failed auth_error"));
        await Task.Delay(1500, TestContext.Current.CancellationToken);
        await session.SendAsync("repaint");
        await EightGoldens.Shoot(session, "greeter-error", renderer);
    }

    [Theory]
    [MemberData(nameof(EightGoldens.Renderers), MemberType = typeof(EightGoldens))]
    public async Task The_ease_of_access_flyout_matches_its_golden(string renderer)
    {
        using var fixture = new EightGreeterFixture();
        fixture.Greetd.Respond(Secret);
        using var session = await Start(fixture, renderer);
        await session.SendAsync("curtain up");
        await session.SendAsync("ease open");
        Assert.NotNull(await session.WaitForAsync(line => line == "EASE open"));
        await Task.Delay(1500, TestContext.Current.CancellationToken);
        await session.SendAsync("repaint");
        await EightGoldens.Shoot(session, "greeter-ease", renderer);
    }

    [Theory]
    [MemberData(nameof(EightGoldens.Renderers), MemberType = typeof(EightGoldens))]
    public async Task The_welcome_screen_matches_its_golden(string renderer)
    {
        using var fixture = new EightGreeterFixture();
        fixture.Greetd.Respond(Secret, new GreetdSuccess(), new GreetdSuccess());
        fixture.Greetd.HoldStartSession();
        using var session = await Start(fixture, renderer);
        await session.SendAsync("curtain up");
        Assert.NotNull(await session.WaitForAsync(line => line == "CURTAIN up command"));
        await session.SendAsync("type secret");
        await session.SendAsync("submit");
        Assert.NotNull(await session.WaitForAsync(line => line == "LOGIN ok"));
        await Task.Delay(1500, TestContext.Current.CancellationToken);
        await session.SendAsync("repaint");
        await EightGoldens.Shoot(session, "greeter-welcome", renderer);
        fixture.Greetd.ReleaseStartSession();
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("START session=", StringComparison.Ordinal)));
    }
}
