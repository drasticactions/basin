using System.Diagnostics;
using Basin;
using Basin.Backend.Drm;
using Basin.Diagnostics;
using Basin.Greetd;
using Basin.Session;
using Basin.Tests;
using Drm;
using Xunit;

namespace BasinGreeter.Tests;

public sealed class HandoffChainTests
{
    private static readonly string? VkmsNode = VkmsCard.Find();

    private sealed class Holder : IDisposable
    {
        private readonly DirectSession _session = new();
        private readonly ISessionDevice _device;

        public Holder()
        {
            _device = _session.OpenDevice(VkmsNode!);
            using var drm = DrmDevice.FromFd(_device.FileDescriptor, ownsFd: false);
            try
            {
                drm.DropMaster();
            }
            catch (DrmException)
            {
            }
        }

        public void Dispose()
        {
            _device.Dispose();
            _session.Dispose();
        }
    }

    private static string Dam()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "dam");
        Assert.SkipUnless(File.Exists(path), "dam was not copied beside the tests");
        return path;
    }

    private static Dictionary<string, string?> DrmEnvironment(Fixture fixture)
    {
        var environment = fixture.Environment;
        environment["BASIN_DRM_DEVICE"] = VkmsNode;
        environment["LIBSEAT_BACKEND"] = "seatd";
        environment["BASIN_GREETER_NO_POWER"] = "1";
        environment["BASIN_HANDOFF"] = null;
        environment["WAYLAND_DISPLAY"] = null;
        return environment;
    }

    private static unsafe (MemoryBuffer Frame, string Output) ReadRetained()
    {
        using var loop = new EpollEventLoop();
        using var session = new DirectSession();
        var backend = new DrmBackend(loop, session, VkmsNode!);
        try
        {
            backend.Start();
            var output = backend.Outputs.First(o => o.Name.StartsWith("Virtual", StringComparison.Ordinal));
            Assert.True(output.TryImportInherited(out var frame, out var reason), reason);
            using (frame)
            {
                var copy = frame.CopyToMemory();
                Assert.NotNull(copy);
                return (copy, output.Name);
            }
        }
        finally
        {
            backend.Dispose();
        }
    }

    private static unsafe double Difference(MemoryBuffer frame, byte[] rgba, int width, int height)
    {
        Assert.Equal((width, height), (frame.Width, frame.Height));
        Assert.True(frame.BeginDataAccess(BufferDataAccess.Read, out var view));
        try
        {
            var differing = 0;
            for (var y = 0; y < height; y++)
            {
                var row = (uint*)((byte*)view.Data + ((long)view.Stride * y));
                for (var x = 0; x < width; x++)
                {
                    var pixel = row[x];
                    var i = ((y * width) + x) * 4;
                    var delta = Math.Max(
                        Math.Abs((int)((pixel >> 16) & 0xff) - rgba[i]),
                        Math.Max(Math.Abs((int)((pixel >> 8) & 0xff) - rgba[i + 1]), Math.Abs((int)(pixel & 0xff) - rgba[i + 2])));
                    differing += delta > 2 ? 1 : 0;
                }
            }

            return (double)differing / (width * height);
        }
        finally
        {
            frame.EndDataAccess();
        }
    }

    [Fact]
    public async Task The_sign_in_frame_stays_lit_and_the_session_inherits_it_without_a_modeset()
    {
        Assert.SkipWhen(VkmsNode is null, "no vkms card node");
        Assert.SkipWhen(!File.Exists("/run/seatd.sock"), "no seatd socket; the greeter takes the card through libseat");
        var dam = Dam();
        using var holder = new Holder();
        using var fixture = new Fixture();
        fixture.Greetd.Respond(new GreetdAuthMessage(GreetdAuthMessageType.Secret, "Password: "), new GreetdSuccess(), new GreetdSuccess());
        fixture.Greetd.HoldStartSession();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.StatePath)!);
        var shot = Path.Combine(Path.GetTempPath(), $"basin-greeter-chain-{Environment.ProcessId}.png");
        using (var greeter = new GreeterSession(
            ["--backend", "drm", "--renderer", "pixman", "--config", fixture.ConfigPath, "--greetd-sock", fixture.Greetd.SocketPath, "--state", fixture.StatePath],
            DrmEnvironment(fixture)))
        {
            await greeter.ExpectAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal), "SOCKET");
            await greeter.SendAsync("clock 12:00");
            await greeter.SendAsync("select ada");
            await greeter.ExpectAsync("AUTH secret Password: ");
            await greeter.SendAsync("type hunter2");
            await greeter.SendAsync("submit");
            await greeter.ExpectAsync("LOGIN ok");
            await greeter.SettleAsync();
            var mark = greeter.Mark();
            await greeter.SendAsync($"shotraw {shot}");
            Assert.NotNull(await greeter.WaitForAsync(line => line.StartsWith($"SHOTRAW {shot}", StringComparison.Ordinal), since: mark));

            fixture.Greetd.ReleaseStartSession();
            await greeter.ExpectAsync("START session=sway");
            var started = Stopwatch.StartNew();
            Assert.True(greeter.WaitForExit(5000), "the greeter did not exit");
            Assert.True(started.ElapsedMilliseconds < 1000, $"the greeter took {started.ElapsedMilliseconds} ms to exit");
            Assert.Equal(0, greeter.Process.ExitCode);
            Assert.Contains(greeter.Lines(), line => line.StartsWith("HANDOFF retain Virtual-1 fb=", StringComparison.Ordinal));
        }

        var start = Assert.IsType<GreetdStartSession>(fixture.Greetd.Requests()[^1]);
        Assert.Contains("BASIN_HANDOFF=retain", start.Env);

        var (retained, output) = ReadRetained();
        try
        {
            var (rgba, width, height) = PngCodec.Decode(File.ReadAllBytes(shot));
            var differing = Difference(retained, rgba, width, height);
            Assert.True(differing < 0.01, $"{differing:P2} of {output} differs from the greeter's sign-in frame");
        }
        finally
        {
            retained.Destroy();
            File.Delete(shot);
        }

        var info = new ProcessStartInfo(dam) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false };
        foreach (var argument in (string[])["--backend", "drm", "--renderer", "pixman", "--frames", "20", "--ipc", "false"])
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in DrmEnvironment(fixture))
        {
            if (value is null)
            {
                info.Environment.Remove(name);
            }
            else
            {
                info.Environment[name] = value;
            }
        }

        foreach (var pair in start.Env)
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            info.Environment[pair[..equals]] = pair[(equals + 1)..];
        }

        using var session = Process.Start(info)!;
        session.StandardInput.Close();
        var errors = session.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        var lines = new List<string>();
        while (await session.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken) is { } line)
        {
            lines.Add(line);
        }

        Assert.True(session.WaitForExit(30_000), "the session did not exit");
        Assert.True(session.ExitCode == 0, $"dam exited {session.ExitCode}:\n{string.Join('\n', lines)}\n{await errors}");
        Assert.Contains(lines, line => line.StartsWith("HANDOFF inherit Virtual-1", StringComparison.Ordinal) && line.EndsWith("modeset=no reason=matched", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("HANDOFF retain Virtual-1 fb=", StringComparison.Ordinal));
    }
}
