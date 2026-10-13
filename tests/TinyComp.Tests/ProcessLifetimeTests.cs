using System.Diagnostics;
using System.IO.Pipes;
using Xunit;

namespace TinyComp.Tests;

public sealed class ProcessLifetimeTests
{
    private static string? Locate(string name)
    {
        var directory = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && directory is not null; i++)
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }

    private static (Process Process, string RuntimeDir) Start(params string[] arguments)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "linux only");
        var path = Locate("tinycomp");
        Assert.SkipWhen(path is null, "the tinycomp binary is not built");
        var runtimeDir = Path.Combine("/tmp", $"basin-proc-{Guid.NewGuid():N}"[..24]);
        Directory.CreateDirectory(runtimeDir);
        File.SetUnixFileMode(runtimeDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var info = new ProcessStartInfo(path!)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in (string[])["--backend", "headless", "--renderer", "pixman", "--config", "false", .. arguments])
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["XDG_RUNTIME_DIR"] = runtimeDir;
        info.Environment["BASIN_MUTE"] = "1";
        info.Environment.Remove("WAYLAND_DISPLAY");
        var process = Process.Start(info)!;
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return (process, runtimeDir);
    }

    private static void Finish(Process process, string runtimeDir)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }

        process.Dispose();
        try
        {
            Directory.Delete(runtimeDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void The_compositor_exits_with_the_commands_status()
    {
        var (process, runtimeDir) = Start("-c", "sh -c 'sleep 0.3; exit 7'", "--exit-with-command");
        try
        {
            Assert.True(process.WaitForExit(20_000), "the compositor kept running after its command exited");
            Assert.Equal(7, process.ExitCode);
        }
        finally
        {
            Finish(process, runtimeDir);
        }
    }

    [Fact]
    public void A_double_forked_setsid_child_is_ended_with_the_compositor()
    {
        var marker = $"{60 + Random.Shared.Next(1000) / 1000.0:F3}";
        var (process, runtimeDir) = Start("--frames", "60", "-c", $"setsid sh -c 'exec sleep {marker}' & sleep 30");
        try
        {
            Assert.True(process.WaitForExit(20_000));
            var clock = Stopwatch.StartNew();
            while (Survivors(marker) > 0 && clock.ElapsedMilliseconds < 5000)
            {
                Thread.Sleep(100);
            }

            Assert.Equal(0, Survivors(marker));
        }
        finally
        {
            Finish(process, runtimeDir);
        }
    }

    private static int Survivors(string marker)
    {
        var count = 0;
        foreach (var directory in Directory.GetDirectories("/proc"))
        {
            try
            {
                var line = File.ReadAllText(Path.Combine(directory, "cmdline")).Replace('\0', ' ');
                if (line.Contains($"sleep {marker}", StringComparison.Ordinal))
                {
                    count++;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return count;
    }

    [Fact]
    public async Task The_ready_fd_gets_ready_once_the_outputs_have_presented()
    {
        using var pipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        var (process, runtimeDir) = Start("--frames", "600", "--ready-fd", pipe.GetClientHandleAsString());
        try
        {
            pipe.DisposeLocalCopyOfClientHandle();
            using var reader = new StreamReader(pipe);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(20_000);
            Assert.Equal("READY=1", await reader.ReadLineAsync(timeout.Token));
            Assert.Null(await reader.ReadLineAsync(timeout.Token));
        }
        finally
        {
            Finish(process, runtimeDir);
        }
    }
}
