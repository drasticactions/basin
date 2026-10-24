using System.Diagnostics;
using Basin.Ipc;
using Xunit;

namespace Basin.Tests;

public sealed class IpcLauncherTests : IDisposable
{
    private readonly DirectoryInfo _runtime = Directory.CreateTempSubdirectory("basin-launcher-");
    private readonly string? _saved = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");

    public IpcLauncherTests() => Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", _runtime.FullName);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", _saved);
        _runtime.Delete(recursive: true);
    }

    private static IpcLauncher Launch(string script, Stream? output = null)
    {
        var launcher = IpcLauncher.Start(["sh", "-c", script], "launch", output ?? Stream.Null, out var error);
        Assert.True(launcher is not null, error);
        return launcher;
    }

    [Fact]
    public async Task The_state_answers_after_stop_for_a_child_that_exited()
    {
        var launcher = Launch("exit 3");
        var pid = launcher.Pid;
        await launcher.Exited.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(launcher.HasExited);
        Assert.Equal(3, launcher.ExitCode);
        Assert.False(await launcher.StopAsync());
        Assert.Equal(pid, launcher.Pid);
        Assert.True(launcher.HasExited);
        Assert.Equal(3, launcher.ExitCode);
        Assert.False(Directory.Exists(launcher.Directory));
    }

    [Fact]
    public async Task The_exit_code_is_null_while_the_child_runs()
    {
        var launcher = Launch("exec sleep 30");
        Assert.Null(launcher.ExitCode);
        Assert.False(launcher.HasExited);
        Assert.False(await launcher.StopAsync());
        Assert.True(launcher.HasExited);
        Assert.NotNull(launcher.ExitCode);
        Assert.False(Directory.Exists($"/proc/{launcher.Pid}/fd"));
    }

    [Fact]
    public async Task Stop_does_not_wait_for_a_grandchild_that_holds_the_output()
    {
        var launcher = Launch("sleep 30 & echo $! > \"$BASIN_IPC_PATH.child\"; exit 0");
        var record = launcher.SocketPath + ".child";
        await launcher.Exited.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var child = int.Parse(File.ReadAllText(record).Trim(), System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            var watch = Stopwatch.StartNew();
            Assert.False(await launcher.StopAsync());
            Assert.InRange(watch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(2));
            Assert.Equal(0, launcher.ExitCode);
        }
        finally
        {
            _ = Signal(child, 9);
        }
    }

    [Fact]
    public async Task Two_pipes_reach_one_stream_as_whole_lines()
    {
        var o = new string('o', 4095);
        var e = new string('e', 4095);
        using var output = new MemoryStream();
        var launcher = Launch($"yes {o} 2>/dev/null | head -n 256 & yes {e} 2>/dev/null | head -n 256 >&2; wait", output);
        await launcher.Exited.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        _ = await launcher.StopAsync();
        Assert.Equal(2 * 1024 * 1024, output.Length);
        var lines = System.Text.Encoding.ASCII.GetString(output.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(512, lines.Length);
        Assert.All(lines, line => Assert.True(line == o || line == e, $"a line of {line.Length} bytes is mixed"));
        Assert.Equal(256, lines.Count(line => line == o));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("/tmp")]
    public void A_name_that_is_not_one_segment_is_refused(string name)
    {
        var marker = Path.Combine(_runtime.FullName, "marker");
        File.WriteAllText(marker, "keep");
        Assert.Null(IpcLauncher.Start(["true"], name, Stream.Null, out var error));
        Assert.Contains("one path segment", error, StringComparison.Ordinal);
        Assert.True(File.Exists(marker));
    }

    [Fact]
    public async Task A_stale_directory_with_only_the_socket_is_replaced()
    {
        var stale = Directory.CreateDirectory(Path.Combine(_runtime.FullName, "launch"));
        File.WriteAllText(Path.Combine(stale.FullName, "basin.sock"), string.Empty);
        var launcher = Launch("exit 0");
        Assert.False(File.Exists(launcher.SocketPath));
        _ = await launcher.StopAsync();
    }

    [Fact]
    public void A_directory_with_other_files_refuses_the_start()
    {
        var stale = Directory.CreateDirectory(Path.Combine(_runtime.FullName, "launch"));
        var kept = Path.Combine(stale.FullName, "notes.txt");
        File.WriteAllText(kept, "keep");
        File.WriteAllText(Path.Combine(stale.FullName, "basin.sock"), string.Empty);
        Assert.Null(IpcLauncher.Start(["true"], "launch", Stream.Null, out var error));
        Assert.Contains("holds more than a stale basin.sock", error, StringComparison.Ordinal);
        Assert.True(File.Exists(kept));
    }

    [Fact]
    public void A_link_at_the_path_refuses_the_start()
    {
        var target = Directory.CreateDirectory(Path.Combine(_runtime.FullName, "target"));
        var kept = Path.Combine(target.FullName, "basin.sock");
        File.WriteAllText(kept, string.Empty);
        _ = Directory.CreateSymbolicLink(Path.Combine(_runtime.FullName, "launch"), target.FullName);
        Assert.Null(IpcLauncher.Start(["true"], "launch", Stream.Null, out var error));
        Assert.Contains("is not a directory", error, StringComparison.Ordinal);
        Assert.True(File.Exists(kept));
    }

    [Fact]
    public void The_owner_lookup_reads_this_user()
    {
        Assert.Equal(UnixSocket.EffectiveUid(), UnixSocket.OwnerUid(_runtime.FullName));
        Assert.Equal(0u, UnixSocket.OwnerUid("/"));
        Assert.Null(UnixSocket.OwnerUid(Path.Combine(_runtime.FullName, "absent")));
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "kill")]
    private static extern int Signal(int pid, int signal);
}
