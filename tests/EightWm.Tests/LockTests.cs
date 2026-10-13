using Xunit;

namespace EightWm.Tests;

public sealed class LockTests
{
    private const string Stub = "read p; if [ \"$p\" = right ]; then echo 1; else echo 0; fi";

    private static string Repository()
    {
        var root = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && root is not null; i++)
        {
            if (File.Exists(Path.Combine(root, "basin.slnx")))
            {
                return root;
            }

            root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        }

        Assert.Skip("the repository root was not found");
        return null!;
    }

    private static string WriteConfig(string extra = "")
    {
        var path = Path.Combine(Path.GetTempPath(), $"eight-lock-{Guid.NewGuid():N}.toml");
        File.WriteAllText(path, "[start]\nscan_desktop_files = false\n" + extra);
        return path;
    }

    private static Dictionary<string, string> Environment() => new()
    {
        ["BASIN_MUTE"] = "1",
        ["BASIN_STILL_CARET"] = "1",
        ["EIGHTWM_LOCK_NAME"] = "Ada Lovelace",
        ["DBUS_SESSION_BUS_ADDRESS"] = "unix:path=/nonexistent/eight-lock-tests/session",
        ["DBUS_SYSTEM_BUS_ADDRESS"] = "unix:path=/nonexistent/eight-lock-tests/system",
    };

    private static EightSession Start(string config, string renderer = "pixman") =>
        new(renderer, EightSession.Compositor(), config, Environment(), "--pam-helper-command", Stub);

    private static async Task<string> State(EightSession session)
    {
        var mark = session.Mark();
        var line = await session.WaitForAsync(line => line.StartsWith("LOCKSTATE ", StringComparison.Ordinal), poke: "lock-state", since: mark);
        Assert.NotNull(line);
        return line!;
    }

    [Fact]
    public async Task Win_L_locks_and_the_right_password_after_a_wrong_one_unlocks()
    {
        var config = WriteConfig();
        try
        {
            using var session = Start(config);
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));
            await session.SendAsync("key Super+l");
            Assert.NotNull(await session.WaitForAsync(line => line == "LOCKED"));
            Assert.Contains("own=on external=off shown=yes curtain=down", await State(session), StringComparison.Ordinal);
            await session.SendAsync("unlock-type wrong");
            await session.SendAsync("unlock-submit");
            Assert.NotNull(await session.WaitForAsync(line => line == "UNLOCK failed 1"));
            Assert.Contains("error=\"The password is incorrect. Try again.\"", await State(session), StringComparison.Ordinal);
            await session.SendAsync("unlock-type right");
            await session.SendAsync("unlock-submit");
            Assert.NotNull(await session.WaitForAsync(line => line == "UNLOCKED"));
            Assert.DoesNotContain(session.Lines(), line => line.Contains("right", StringComparison.Ordinal) && !line.StartsWith("OPTIONS", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(config);
        }
    }

    [Fact]
    public async Task A_swipe_raises_the_lock_keyboard_and_a_password_typed_on_it_unlocks()
    {
        var config = WriteConfig();
        try
        {
            using var session = Start(config);
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));
            await session.SendAsync("lock");
            Assert.NotNull(await session.WaitForAsync(line => line == "LOCKED"));
            await Task.Delay(1500, TestContext.Current.CancellationToken);
            await session.SendAsync("touchdrag 0.5 0.8 0 -0.5");
            Assert.NotNull(await session.WaitForAsync(line => line == "CURTAIN up swipe"));
            Assert.NotNull(await session.WaitForAsync(line => line == "LOCKKEYBOARD on"));
            await session.SendAsync("lock-keyboard-type right");
            await Task.Delay(500, TestContext.Current.CancellationToken);
            await session.SendAsync("unlock-submit");
            Assert.NotNull(await session.WaitForAsync(line => line == "UNLOCKED"));
        }
        finally
        {
            File.Delete(config);
        }
    }

    [Fact]
    public async Task A_tap_off_the_password_box_hides_the_lock_keyboard_and_a_tap_on_it_brings_it_back()
    {
        var config = WriteConfig();
        try
        {
            using var session = Start(config);
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));
            await session.SendAsync("lock");
            Assert.NotNull(await session.WaitForAsync(line => line == "LOCKED"));
            await Task.Delay(1500, TestContext.Current.CancellationToken);
            await session.SendAsync("touchdrag 0.5 0.8 0 -0.5");
            Assert.NotNull(await session.WaitForAsync(line => line == "LOCKKEYBOARD on"));

            var mark = session.Mark();
            await session.SendAsync("touch 0.5 0.08");
            Assert.NotNull(await session.WaitForAsync(line => line == "LOCKKEYBOARD off", since: mark));
            mark = session.Mark();
            await session.SendAsync("touch 0.2 0.5");
            Assert.Null(await session.WaitForAsync(line => line == "LOCKKEYBOARD on", since: mark, attempts: 15));

            var located = await session.WaitForAsync(line => line.StartsWith("LOCKAT password ", StringComparison.Ordinal) && !line.EndsWith(" none", StringComparison.Ordinal), poke: "lock-locate-password", since: mark);
            Assert.NotNull(located);
            var parts = located!.Split(' ');
            Assert.NotEqual("none", parts[2]);
            await session.SendAsync($"touch {parts[2]} {parts[3]}");
            Assert.NotNull(await session.WaitForAsync(line => line == "LOCKKEYBOARD on", since: mark));
        }
        finally
        {
            File.Delete(config);
        }
    }

    [Fact]
    public async Task Idle_locks_after_the_configured_seconds()
    {
        var config = WriteConfig("\n[lock]\nidle_seconds = 1\n");
        try
        {
            using var session = Start(config);
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));
            Assert.NotNull(await session.WaitForAsync(line => line == "LOCKED", attempts: 60));
            await Task.Delay(1500, TestContext.Current.CancellationToken);
            var status = await session.WaitForAsync(line => line.StartsWith("OK idle/status ", StringComparison.Ordinal), poke: "ipc idle/status {}");
            Assert.NotNull(status);
            var idle = int.Parse(status!.Split(' ')[2]["idle_ms=".Length..], System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(idle >= 2000, status);
        }
        finally
        {
            File.Delete(config);
        }
    }

    [Fact]
    public async Task Five_failures_lock_out_further_tries()
    {
        var config = WriteConfig();
        try
        {
            using var session = Start(config);
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));
            await session.SendAsync("lock");
            Assert.NotNull(await session.WaitForAsync(line => line == "LOCKED"));
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                await session.SendAsync("unlock-type wrong");
                await session.SendAsync("unlock-submit");
                Assert.NotNull(await session.WaitForAsync(line => line == $"UNLOCK failed {attempt}"));
            }

            Assert.NotNull(await session.WaitForAsync(line => line == "UNLOCK lockout"));
            var mark = session.Mark();
            await session.SendAsync("unlock-type right");
            await session.SendAsync("unlock-submit");
            Assert.Null(await session.WaitForAsync(line => line == "UNLOCKED" || line == "UNLOCK checking", attempts: 10, since: mark));
            Assert.Contains("busy=yes", await State(session), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(config);
        }
    }

    [Fact]
    public async Task An_external_locker_covers_the_own_lock_and_hands_it_back()
    {
        var root = Repository();
        var config = WriteConfig();
        try
        {
            using var session = Start(config);
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));
            await session.SendAsync("lock");
            Assert.NotNull(await session.WaitForAsync(line => line == "LOCKED"));
            var mark = session.Mark();
            await session.SendAsync($"launch {Path.Combine(root, "scripts", "session-lock.sh")} 2");
            Assert.NotNull(await session.WaitForAsync(line => line == "LOCKED", since: mark, attempts: 200));
            Assert.Contains("own=on external=on shown=no", await State(session), StringComparison.Ordinal);
            Assert.NotNull(await session.WaitForAsync(line => line == "UNLOCKED external", since: mark, attempts: 200));
            Assert.Contains("own=on external=off shown=yes", await State(session), StringComparison.Ordinal);
            await session.SendAsync("unlock-type right");
            await session.SendAsync("unlock-submit");
            Assert.NotNull(await session.WaitForAsync(line => line == "UNLOCKED", since: mark));
        }
        finally
        {
            File.Delete(config);
        }
    }

    [Theory]
    [MemberData(nameof(EightGoldens.Renderers), MemberType = typeof(EightGoldens))]
    public async Task The_own_lock_curtain_and_sign_in_match_their_goldens(string renderer)
    {
        EightGoldens.SkipUnlessRenderer(renderer);
        var config = WriteConfig();
        try
        {
            using var session = Start(config, renderer);
            await EightGoldens.RequireRenderer(session, renderer);
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));
            await session.SendAsync("lock");
            Assert.NotNull(await session.WaitForAsync(line => line == "LOCKED"));
            await session.SendAsync("lock-clock 9:41");
            await session.SendAsync("move 5000 5000");
            await EightGoldens.Shoot(session, "lock-curtain", renderer);
            await session.SendAsync("unlock-type wrong");
            await session.SendAsync("unlock-submit");
            Assert.NotNull(await session.WaitForAsync(line => line == "UNLOCK failed 1"));
            await Task.Delay(1500, TestContext.Current.CancellationToken);
            await EightGoldens.Shoot(session, "lock-error", renderer);
        }
        finally
        {
            File.Delete(config);
        }
    }
}
