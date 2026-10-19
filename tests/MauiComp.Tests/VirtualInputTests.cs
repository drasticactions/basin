using Xunit;

namespace MauiComp.Tests;

public sealed class VirtualInputTests
{
    [Fact]
    public async Task A_virtual_pointer_moves_the_pointer()
    {
        var (compositor, _) = HeadlessSessionTests.Require();
        var vptr = Session.WlClient("vptr");
        Assert.SkipWhen(vptr is null, "scripts/wlclients/bin/vptr cannot be built");
        using var session = new Session(compositor);
        var display = await session.DisplayAsync();
        var before = await session.WaitForAsync(line => line.StartsWith("POINTER ", StringComparison.Ordinal), poke: "where");
        Assert.NotNull(before);

        using (var client = Session.StartClient(vptr!, display, "25", "50", "100", "100", "0"))
        {
            client.WaitForExit(10_000);
        }

        var after = await session.WaitForAsync(line => line.StartsWith("POINTER ", StringComparison.Ordinal) && line != before, poke: "where", fresh: true);
        Assert.True(after is not null, $"the pointer stayed at {before}");
    }
}
