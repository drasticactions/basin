using Xunit;

namespace EightWm.Tests;

public sealed class OutputLossTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("eight-outputs-");

    public void Dispose() => _dir.Delete(recursive: true);

    private EightSession Start(params string[] extra)
    {
        var config = Path.Combine(_dir.FullName, "eight-wm.toml");
        File.WriteAllText(config, "[start]\nscan_desktop_files = false\n");
        var environment = new Dictionary<string, string>
        {
            ["BASIN_MUTE"] = "1",
            ["XDG_STATE_HOME"] = Path.Combine(_dir.FullName, "state"),
            ["DBUS_SESSION_BUS_ADDRESS"] = "unix:path=/nonexistent/eight-outputs-tests/session",
            ["DBUS_SYSTEM_BUS_ADDRESS"] = "unix:path=/nonexistent/eight-outputs-tests/system",
        };
        return new EightSession(EightSession.Compositor(), config, environment, ["--client", KeyboardTests.WlClient("ssdwin"), .. extra]);
    }

    [Fact]
    public async Task Losing_the_only_output_parks_the_apps_and_a_new_output_brings_them_back()
    {
        var ssdwin = KeyboardTests.WlClient("ssdwin");
        using var session = Start();
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("APP + ", StringComparison.Ordinal), attempts: 150));
        await session.SendAsync("unplug");
        Assert.NotNull(await session.WaitForAsync(line => line == "EVACUATE HEADLESS-1 apps=1 parked=1"));
        await session.SendAsync("move 10 10");
        await session.SendAsync("click");
        await session.SendAsync("key a");
        var mark = session.Mark();
        await session.SendAsync($"launch {ssdwin}");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("APP + ", StringComparison.Ordinal), since: mark, attempts: 150));
        await session.SendAsync("plug");
        Assert.NotNull(await session.WaitForAsync(line => line == "UNPARK HEADLESS-2 apps=2"));
        var mru = await session.WaitForAsync(line => line.StartsWith("MRU output=0 ", StringComparison.Ordinal), poke: "mru");
        Assert.NotNull(mru);
        Assert.Contains("start=off", mru, StringComparison.Ordinal);
        Assert.Contains("mru=[ssdwin,ssdwin]", mru, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Losing_the_output_an_app_is_on_moves_it_to_the_one_that_remains()
    {
        using var session = Start("--outputs", "2");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("APP + ", StringComparison.Ordinal), attempts: 150));
        await session.SendAsync("unplug 0");
        Assert.NotNull(await session.WaitForAsync(line => line == "EVACUATE HEADLESS-1 apps=1 parked=0"));
        var mru = await session.WaitForAsync(line => line.StartsWith("MRU output=0 ", StringComparison.Ordinal), poke: "mru");
        Assert.NotNull(mru);
        Assert.Contains("cells=[ssdwin]", mru, StringComparison.Ordinal);
        var where = await session.WaitForAsync(line => line.Contains("app ssdwin ", StringComparison.Ordinal), poke: "where");
        Assert.NotNull(where);
        Assert.Contains("enabled=True parked=False", where, StringComparison.Ordinal);
    }
}
