using Xunit;

namespace System7Wm.Tests;

public sealed class VirtualInputTests
{
    [Fact]
    public async Task A_virtual_pointer_moves_the_pointer()
    {
        var vptr = Session.WlClient("vptr");
        Assert.SkipWhen(vptr is null, "scripts/wlclients/bin/vptr cannot be built");
        using var session = new Session(Prerequisites.Compositor());
        var display = await session.DisplayAsync();
        await session.SettleAsync();

        using var client = session.StartClient(vptr!, display, null, "300", "200", "1024", "768", "0");
        var moved = await session.WaitForAsync(line => line == "POINTER 300 200", poke: "where", fresh: true, attempts: 40);
        Assert.True(moved is not null, $"the pointer did not move: {string.Join(" | ", session.LinesSince(0).Where(l => l.StartsWith("POINTER", StringComparison.Ordinal)))}");
        await session.QuitCleanlyAsync();
    }
}
