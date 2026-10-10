using System.Collections.Concurrent;
using System.Diagnostics;
using Basin.Hosted;
using Basin.Scene;
using Basin.Shell.Xdg;
using Xunit;

#pragma warning disable CA1416

namespace Basin.Transport.Waypipe.Windows.Tests;

public sealed class WslLiveTests
{
    [Fact]
    public void Stock_waypipe_in_WSL2_maps_a_toplevel_through_a_listener_on_the_WSL_VM()
    {
        Assert.SkipWhen(SkipReason() is { } reason, SkipReason() ?? string.Empty);
        using var rig = new Rig();
        var listener = rig.Bind();
        Assert.Equal(CurrentVm(), listener.LocalEndPoint.VmId);

        using var client = Launch(listener.LocalEndPoint.VsockPort!.Value);
        try
        {
            rig.PumpUntil(() => rig.Opened == 1 && rig.Mapped is not null && rig.Commits >= 10, TimeSpan.FromSeconds(60));
            Assert.Equal(1, listener.Accepted);
            Assert.Equal(1, listener.Live);
            Assert.Equal(1, rig.Adopted);
        }
        finally
        {
            Stop(client, listener.LocalEndPoint.VsockPort!.Value);
        }

        rig.PumpUntil(() => listener.Live == 0 && rig.Ended == 1, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void A_WSL_restart_rebinds_on_the_new_VM_and_a_second_client_connects()
    {
        Assert.SkipWhen(SkipReason() is { } reason, SkipReason() ?? string.Empty);
        using var rig = new Rig();
        var listener = rig.Bind();
        var before = listener.LocalEndPoint.VmId;
        HyperVEndPoint? rebound = null;
        listener.Rebound += local => rebound = local;
        Exception? failed = null;
        listener.Failed += error => failed = error;

        Run("--shutdown");
        rig.PumpUntil(() => failed is not null || !Running(), TimeSpan.FromSeconds(30));
        rig.PumpFor(TimeSpan.FromSeconds(35));
        Run("--", "true");
        rig.PumpUntil(() => failed is not null || rebound is not null, TimeSpan.FromSeconds(90));
        Assert.Null(failed);
        Assert.NotNull(rebound);
        Assert.NotEqual(before, rebound.VmId);
        Assert.Equal(CurrentVm(), rebound.VmId);

        using var client = Launch(rebound.VsockPort!.Value);
        try
        {
            rig.PumpUntil(() => rig.Opened == 1 && rig.Mapped is not null, TimeSpan.FromSeconds(60));
        }
        finally
        {
            Stop(client, rebound.VsockPort!.Value);
        }
    }

    private static Guid CurrentVm() => WslVm.QueryIdAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();

    private static bool Running() => WslVm.IsRunningAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();

    private static string? SkipReason()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "the live WSL2 rows run on Windows";
        }

        if (Environment.GetEnvironmentVariable("BASIN_WSL") != "1")
        {
            return "set BASIN_WSL=1 to run the live WSL2 rows";
        }

        try
        {
            var (exit, output) = Run("--", "sh", "-c", "command -v waypipe && command -v weston-simple-shm");
            return exit == 0 ? null : $"the WSL distro lacks waypipe or weston-simple-shm: {output}";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return $"wsl.exe did not run: {ex.Message}";
        }
    }

    private static Process Launch(uint port)
    {
        var start = new ProcessStartInfo("wsl.exe") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in new[] { "--", "waypipe", "--vsock", "-s", port.ToString(System.Globalization.CultureInfo.InvariantCulture), "--compress", "lz4", "--no-gpu", "server", "--", "weston-simple-shm" })
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["WSL_UTF8"] = "1";
        var process = Process.Start(start) ?? throw new InvalidOperationException("wsl.exe did not start");
        process.ErrorDataReceived += (_, line) =>
        {
            if (line.Data is { Length: > 0 } text)
            {
                TestContext.Current.SendDiagnosticMessage($"waypipe: {text}");
                Console.Error.WriteLine($"waypipe: {text}");
            }
        };
        process.BeginErrorReadLine();
        return process;
    }

    private static void Stop(Process client, uint port)
    {
        Run("--", "pkill", "-f", $"waypipe --vsock -s {port}");
        if (!client.WaitForExit(10_000))
        {
            client.Kill(entireProcessTree: true);
        }
    }

    private static (int Exit, string Output) Run(params string[] arguments)
    {
        var start = new ProcessStartInfo("wsl.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["WSL_UTF8"] = "1";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("wsl.exe did not start");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output.Trim());
    }

    private sealed class Rig : IDisposable
    {
        private readonly ConcurrentQueue<Action> _posted = new();
        private readonly BasinCompositorHost _host;
        private readonly BasinViewOutput _view;
        private readonly WaypipeAdoption _adoption;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly List<SceneSurface> _surfaces = [];
        private HyperVWaypipeListener? _listener;
        private long _nextFrame;

        public Rig()
        {
            _host = new BasinCompositorHost(new BasinCompositorOptions
            {
                AppName = "basin-wsl-test",
                Dmabuf = false,
                ConfigureServices = (_, services) => services
                    .Without("zwlr_layer_shell_v1")
                    .Without("ext_session_lock_manager_v1"),
            });
            _view = _host.CreateViewOutput(800, 600);
            _adoption = new WaypipeAdoption(
                _host.Display,
                _host.Services.Require<ClientBufferRegistry>(),
                _host.Services.Require<CompositorGlobal>(),
                hostHasDmabuf: false);
            _host.Shell.NewToplevel += toplevel =>
            {
                Mapped ??= toplevel;
                _surfaces.Add(new SceneSurface(_host.Scene.Root, toplevel.Surface));
                toplevel.Surface.Committed += () => Commits++;
            };
        }

        public int Opened { get; private set; }

        public int Ended { get; private set; }

        public int Adopted { get; private set; }

        public int Commits { get; private set; }

        public XdgToplevelWindow? Mapped { get; private set; }

        public HyperVWaypipeListener Bind()
        {
            _listener = HyperVWaypipeListener.BindAnyPortAsync(HyperVVm.Wsl, _posted.Enqueue, cancellation: TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            _listener.ChannelAccepted += channel =>
            {
                channel.Opened += () => _posted.Enqueue(() => Opened++);
                channel.Ended += _ => _posted.Enqueue(() => Ended++);
                _adoption.Adopt(channel);
                Adopted++;
            };
            return _listener;
        }

        public void PumpUntil(Func<bool> condition, TimeSpan limit)
        {
            var deadline = _clock.Elapsed + limit;
            while (!condition())
            {
                Assert.True(_clock.Elapsed < deadline, $"the condition was never reached while pumping: accepted={_listener?.Accepted} live={_listener?.Live} adopted={Adopted} opened={Opened} ended={Ended} mapped={Mapped is not null} commits={Commits}");
                Pump();
            }
        }

        public void PumpFor(TimeSpan span)
        {
            var until = _clock.Elapsed + span;
            while (_clock.Elapsed < until)
            {
                Pump();
            }
        }

        public void Dispose()
        {
            _listener?.Dispose();
            while (_posted.TryDequeue(out var action))
            {
                action();
            }

            _adoption.Dispose();
            _view.Dispose();
            _host.Dispose();
        }

        private void Pump()
        {
            _host.Loop.Dispatch(5);
            while (_posted.TryDequeue(out var action))
            {
                action();
            }

            var now = _clock.ElapsedTicks;
            if (now >= _nextFrame)
            {
                _nextFrame = now + (Stopwatch.Frequency / 60);
                if (_host.EnterFrame(_clock.Elapsed))
                {
                    try
                    {
                        _host.Session.MarkPresented(_view.SceneOutput);
                        _host.Scene.SendFrameDone((uint)Environment.TickCount);
                    }
                    finally
                    {
                        _host.ExitFrame();
                    }
                }
            }

            _host.Display.FlushClients();
        }
    }
}
