using System.Diagnostics;
using Xunit;

namespace BasinGreeter.Tests;

public sealed class ReaderTests
{
    private const uint LeftMeta = 125;
    private const uint LeftAlt = 56;
    private const uint KeyS = 31;

    private static string Stub(Fixture fixture, string log) => fixture.Write("reader.sh", $$"""
        #!/bin/sh
        echo "started $$" >> {{log}}
        wayland-info > {{log}}.globals 2>&1
        trap 'echo stopped >> {{log}}; exit 0' TERM
        while :; do sleep 0.1; done
        """);

    private static async Task<string> WaitFor(string path, string text)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (File.Exists(path) && File.ReadAllText(path).Contains(text, StringComparison.Ordinal))
            {
                return File.ReadAllText(path);
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"{path} never held '{text}': {(File.Exists(path) ? File.ReadAllText(path) : "missing")}");
        return "";
    }

    [Fact]
    public async Task The_screen_reader_starts_and_stops_by_command_and_chord_and_alone_sees_the_globals()
    {
        Assert.SkipWhen(GreeterSession.Which("wayland-info") is null, "wayland-info is not installed");
        using var bus = await AccessibilityBus.StartAsync();
        using var fixture = new Fixture();
        var log = Path.Combine(fixture.Root, "reader.log");
        var stub = Stub(fixture, log);
        File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.WriteAllText(fixture.ConfigPath, File.ReadAllText(fixture.ConfigPath) + $"\n[accessibility]\nscreen_reader = \"toggle\"\norca = [\"{stub}\"]\n");
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.StatePath)!);
        using var greeter = new GreeterSession(GreeterSession.Headless("pixman", fixture.ConfigPath, "--greetd-sock", fixture.Greetd.SocketPath, "--state", fixture.StatePath), bus.Apply(fixture.Environment));
        var socket = (await greeter.ExpectAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal), "SOCKET")).Split(' ')[1];
        await greeter.ExpectAsync("A11Y on");

        await greeter.SendAsync("reader on");
        await greeter.ExpectAsync(line => line.StartsWith("READER on pid=", StringComparison.Ordinal), "the reader started");
        await WaitFor(log, "started");
        var globals = await WaitFor(log + ".globals", "xdg_wm_base");
        Assert.Contains("wl_compositor", globals, StringComparison.Ordinal);

        var outside = new ProcessStartInfo("wayland-info") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        outside.Environment["WAYLAND_DISPLAY"] = socket;
        outside.Environment["XDG_RUNTIME_DIR"] = bus.Runtime;
        using (var probe = Process.Start(outside)!)
        {
            var seen = await probe.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            Assert.True(probe.WaitForExit(5000));
            Assert.DoesNotContain("wl_compositor", seen, StringComparison.Ordinal);
            Assert.DoesNotContain("xdg_wm_base", seen, StringComparison.Ordinal);
        }

        foreach (var (code, pressed) in new[] { (LeftMeta, 1), (LeftAlt, 1), (KeyS, 1), (KeyS, 0), (LeftAlt, 0), (LeftMeta, 0) })
        {
            await greeter.SendAsync($"key {code} {pressed}");
        }

        await greeter.ExpectAsync("READER off");
        await WaitFor(log, "stopped");

        foreach (var (code, pressed) in new[] { (LeftMeta, 1), (LeftAlt, 1), (KeyS, 1), (KeyS, 0), (LeftAlt, 0), (LeftMeta, 0) })
        {
            await greeter.SendAsync($"key {code} {pressed}");
        }

        var second = greeter.Mark();
        await greeter.ExpectAsync(line => line.StartsWith("READER on pid=", StringComparison.Ordinal), "the chord starts it again", since: second);
    }

    [Fact]
    public async Task Without_an_accessibility_bus_there_is_no_reader_and_a_login_stops_a_running_one()
    {
        using (var fixture = new Fixture("[accessibility]\nscreen_reader = \"on\"\norca = [\"/bin/true\"]\n"))
        using (var greeter = fixture.Start())
        {
            await greeter.ExpectAsync("A11Y off");
            await greeter.SendAsync("reader on");
            await greeter.ExpectAsync("READERSTATE off available=no");
        }

        using var bus = await AccessibilityBus.StartAsync();
        using var login = new Fixture();
        var log = Path.Combine(login.Root, "reader.log");
        var stub = Stub(login, log);
        File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.WriteAllText(login.ConfigPath, File.ReadAllText(login.ConfigPath) + $"\n[accessibility]\nscreen_reader = \"on\"\norca = [\"{stub}\"]\n");
        login.Greetd.Respond(new Basin.Greetd.GreetdAuthMessage(Basin.Greetd.GreetdAuthMessageType.Secret, "Password: "), new Basin.Greetd.GreetdSuccess(), new Basin.Greetd.GreetdSuccess());
        Directory.CreateDirectory(Path.GetDirectoryName(login.StatePath)!);
        using var session = new GreeterSession(GreeterSession.Headless("pixman", login.ConfigPath, "--greetd-sock", login.Greetd.SocketPath, "--state", login.StatePath), bus.Apply(login.Environment));
        await session.ExpectAsync(line => line.StartsWith("READER on pid=", StringComparison.Ordinal), "the reader at start");
        await WaitFor(log, "started");
        await session.SendAsync("select ada");
        await session.ExpectAsync("AUTH secret Password: ");
        await session.SendAsync("type pw");
        await session.SendAsync("submit");
        await session.ExpectAsync("EXIT start");
        var pid = int.Parse((await WaitFor(log, "started")).Split(' ', '\n')[1], System.Globalization.CultureInfo.InvariantCulture);
        for (var attempt = 0; attempt < 100 && Directory.Exists($"/proc/{pid}"); attempt++)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.False(Directory.Exists($"/proc/{pid}"), $"the reader {pid} outlived the login");
    }

    [Fact]
    public async Task Super_alt_8_zooms_the_greeter_and_super_alt_c_draws_the_screen_curtain()
    {
        using var fixture = new Fixture("[accessibility]\nscreen_reader = \"off\"\n");
        using var greeter = fixture.Start();
        async Task AccessChord(uint key)
        {
            foreach (var (code, pressed) in new[] { (LeftMeta, 1), (LeftAlt, 1), (key, 1), (key, 0), (LeftAlt, 0), (LeftMeta, 0) })
            {
                await greeter.SendAsync($"key {code} {pressed}");
            }
        }

        await AccessChord(9);
        await greeter.ExpectAsync("ZOOM factor=2");
        await AccessChord(46);
        await greeter.ExpectAsync("CURTAIN on");
        await AccessChord(46);
        await greeter.ExpectAsync("CURTAIN off");
        await AccessChord(12);
        await greeter.ExpectAsync("ZOOM factor=1.6");
    }
}
