using System.CommandLine;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Threading;
using Basin;
using Basin.Avalonia;
using Basin.Cli;
using Basin.Diagnostics;
using Basin.Scene;

namespace Waylonia;

internal sealed class WayloniaApp : Application
{
    private static WayloniaRun? _run;
    private static int _exitStatus;
    private static long _rendered;

    private BasinOutputView? _view;
    private BasinCompositorHost? _host;
    private ToplevelWindows? _windows;
    private HostClipboard? _clipboard;
    private HostDrag? _hostDrag;
    private AvaloniaTextInput? _textInput;
    private Process? _client;
    private Window? _window;
    private TrayIcon? _tray;
    private IDisposable? _globalHotkeys;
    private readonly List<Process> _launched = [];
    private bool _shuttingDown;

    public static long Rendered => Interlocked.Read(ref _rendered);

    public override void Initialize() => Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());

    public static int Run(WayloniaRun run)
    {
        _run = run;
        _exitStatus = 0;
        var builder = AppBuilder.Configure<WayloniaApp>().UsePlatformDetect().UseHostWindowing()
            .With(new MacOSPlatformOptions { ShowInDock = false });
        var status = builder.StartWithClassicDesktopLifetime([]);
        return status != 0 ? status : _exitStatus;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _view = new BasinOutputView(CreateHost, createOwnView: false);
        _view.HostReady += OnHostReady;
        _view.HostFailed += error =>
        {
            Console.Error.WriteLine($"the compositor host could not start: {error.Message}");
            _ = ShutdownAsync(1);
        };
        _window = new Window
        {
            Width = 1,
            Height = 1,
            Title = "Waylonia",
            Content = _view,
            WindowDecorations = WindowDecorations.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            CanResize = false,
            Background = global::Avalonia.Media.Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
        };
        _window.Closing += (_, e) =>
        {
            if (!_shuttingDown)
            {
                e.Cancel = true;
                _ = ShutdownAsync(0);
            }
        };

        if (_run!.Tray)
        {
            var quit = new NativeMenuItem("Quit Waylonia");
            quit.Click += (_, _) => _ = ShutdownAsync(0);
            _tray = new TrayIcon
            {
                Icon = new WindowIcon(typeof(WayloniaApp).Assembly.GetManifestResourceStream("Waylonia.Wayland_Logo.png")!),
                ToolTipText = "Waylonia — starting…",
                Menu = new NativeMenu { Items = { quit } },
            };
            TrayIcon.SetIcons(this, [_tray]);
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _window;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        _signals.Add(System.Runtime.InteropServices.PosixSignalRegistration.Create(
            System.Runtime.InteropServices.PosixSignal.SIGINT, OnPosixSignal));
        _signals.Add(System.Runtime.InteropServices.PosixSignalRegistration.Create(
            System.Runtime.InteropServices.PosixSignal.SIGTERM, OnPosixSignal));

        base.OnFrameworkInitializationCompleted();
    }

    private readonly List<System.Runtime.InteropServices.PosixSignalRegistration> _signals = [];

    private void OnPosixSignal(System.Runtime.InteropServices.PosixSignalContext context)
    {
        context.Cancel = true;
        Dispatcher.UIThread.Post(() => _ = ShutdownAsync(0));
    }

    private Basin.IProtocolModule? _xwayland;

    private BasinCompositorHost CreateHost()
    {
        _textInput = new AvaloniaTextInput(action => _view!.Post(action));
        _xwayland = OperatingSystem.IsLinux() && _run!.XWayland && _run.WaypipeListen is null && _run.SshHost is null
            ? WayloniaXWayland.TryCreateModule()
            : null;
        var host = new BasinCompositorHost(new BasinCompositorOptions
        {
            AppName = "waylonia",
            SocketName = _run!.SocketName,
            ManagedTransport = _run.WaypipeListen is not null || _run.SshHost is not null || !OperatingSystem.IsLinux(),
            TextInput = _textInput,
            ExtraModules = _xwayland is { } xwayland ? [xwayland] : null,
        });
        _windows = new ToplevelWindows(host, action => _view!.Post(action), requestFrame: () => _view?.RequestFrame());
        if (_run.FollowCursor)
        {
            _windows.Policy = new CursorScreenPolicy();
        }

        _windows.CountChanged += count => UpdateStatus($"{count} client window(s) on {host.Socket}");
        if (_run.Drag)
        {
            _hostDrag = new HostDrag(host);
            _windows.AttachDrag(_hostDrag);
        }
        _windows.AttachTextInput(_textInput);
        if (_xwayland is { } attachXwayland)
        {
            WayloniaXWayland.Attach(attachXwayland, host, _windows);
        }
        if (_run.Clipboard)
        {
            _clipboard = new HostClipboard(
                host,
                () => _window is { } window ? global::Avalonia.Controls.TopLevel.GetTopLevel(window)?.Clipboard : null,
                action => _view!.Post(action));
            _windows.WindowActivatedOnHost += () => _ = _clipboard!.PushFromHostAsync();
        }
        host.Composited += OnComposited;
        _host = host;
        return host;
    }

    private void OnHostReady(BasinCompositorHost host)
    {
        if (_window is { } window)
        {
            var screens = window.Screens;
            var scaleSettled = false;
            void Publish()
            {
                var snapshot = HostScreens.Capture(screens);
                var key = HostScreens.KeyFor(screens, screens.ScreenFromWindow(window));
                var scale = window.RenderScaling;
                var noteScale = scale > 0 && (scaleSettled || scale != 1.0);
                _view?.Post(() =>
                {
                    host.Screens.Apply(snapshot);
                    if (OperatingSystem.IsMacOS())
                    {
                        foreach (var info in snapshot)
                        {
                            if (MacScreenScales.TryGetScale(info) is { } known)
                            {
                                host.Screens.NoteWindowScale(info.Key, known);
                            }
                        }
                    }

                    if (key is not null && noteScale)
                    {
                        host.Screens.NoteWindowScale(key, scale);
                    }
                });
            }

            screens.Changed += (_, _) => Publish();
            window.ScalingChanged += (_, _) =>
            {
                scaleSettled = true;
                Publish();
            };
            Publish();
            window.Hide();
        }

        Console.WriteLine($"SOCKET {host.Socket}");
        if (_run!.Hotkeys.Count > 0 && _window is { } anchor)
        {
            if (host.Socket.Length == 0 && _run.SshHost is null)
            {
                BasinLog.Warn($"this session has no local socket, global hotkeys are off");
            }
            else
            {
                _globalHotkeys = GlobalHotkeys.TryStart(
                    _run.Hotkeys, anchor, _view!, host, hotkey => LaunchHotkey(host, hotkey));
            }
        }

        if (WayloniaXWayland.DisplayName(host) is { } xdisplay)
        {
            Console.WriteLine($"XWAYLAND {xdisplay}");
            Environment.SetEnvironmentVariable("DISPLAY", xdisplay);
        }

        UpdateStatus($"waiting for clients on {host.Socket}");

        if (_run!.WaypipeListen is { } endpoint)
        {
            _ = AcceptChannelAsync(host, endpoint);
        }
        else if (_run.SshHost is { } sshHost)
        {
            _ = LaunchSshAsync(host, sshHost, _run.SshCommand);
        }

        if (_run!.Command is { } command)
        {
            _client = BasinDiagnostics.StartClient(command, host.Socket);
            if (_client is null)
            {
                Console.Error.WriteLine($"failed to start '{command}'");
                _ = ShutdownAsync(1);
            }
        }
    }

    private void LaunchHotkey(BasinCompositorHost host, Hotkey hotkey)
    {
        if (_shuttingDown)
        {
            return;
        }

        var remote = _run!.SshHost;
        Process? client;
        try
        {
            if (remote is null)
            {
                client = BasinDiagnostics.StartClient(hotkey.Command, host.Socket);
            }
            else
            {
                if (_ssh is null or { HasExited: true })
                {
                    if (!StartForward(host, remote))
                    {
                        return;
                    }

                    BasinLog.Info($"the connection to {remote} was gone; opened it again");
                    UpdateStatus($"reconnecting to {remote}");
                    _ = WatchForwardAsync(remote);
                }

                client = StartRemoteClient(remote, hotkey.Command);
            }
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            BasinLog.Warn($"hotkey '{hotkey.Chord}': '{hotkey.Command}' failed to start: {error.Message}");
            return;
        }

        if (client is null)
        {
            return;
        }

        _launched.Add(client);
        UpdateStatus(remote is null
            ? $"started '{hotkey.Command}'"
            : $"started '{hotkey.Command}' on {remote}");
    }

    private Process? StartRemoteClient(string sshHost, string command)
    {
        if (_sshDisplayName is not { } displayName)
        {
            BasinLog.Warn($"'{command}' cannot start on {sshHost}: the remote session is not up");
            return null;
        }

        var quoted = command.Replace("'", "'\\''");
        var info = new ProcessStartInfo("ssh")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        info.ArgumentList.Add("-o");
        info.ArgumentList.Add("BatchMode=yes");
        if (_sshControlPath is { } control)
        {
            info.ArgumentList.Add("-o");
            info.ArgumentList.Add($"ControlPath={control}");
            info.ArgumentList.Add("-o");
            info.ArgumentList.Add("ControlMaster=no");
        }

        info.ArgumentList.Add(sshHost);
        info.ArgumentList.Add(
            $"d=\"$XDG_RUNTIME_DIR/{displayName}\"; i=0; " +
            $"while [ ! -S \"$d\" ] && [ $i -lt 50 ]; do sleep 0.2; i=$((i+1)); done; " +
            $"if [ -s {_sshXDisplayFile} ]; then DISPLAY=$(cat {_sshXDisplayFile}); export DISPLAY; fi; " +
            $"WAYLAND_DISPLAY={displayName} sh -c '{quoted}'");
        var started = Process.Start(info);
        if (started is null)
        {
            return null;
        }

        var relay = new Relay(command);
        relay.Watch(started.StandardOutput);
        relay.Watch(started.StandardError);
        _ = WatchClientAsync(started, relay, command);
        return started;
    }

    private void UpdateStatus(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_tray is not null)
            {
                _tray.ToolTipText = $"Waylonia — {text}";
            }
        });
    }

    private void OnComposited(long composited)
    {
        Interlocked.Exchange(ref _rendered, composited);
        var run = _run!;
        if (run.Frames > 0 && composited >= run.Frames && !_shuttingDown)
        {
            Dispatcher.UIThread.Post(() => _ = ShutdownAsync(0));
        }
    }

    private readonly List<Basin.Transport.Waypipe.WaypipeChannel> _channels = [];
    private LinuxDmabufGlobal? _channelDmabuf;
    private System.Net.Sockets.Socket? _channelListener;
    private Process? _ssh;
    private Relay? _sshRelay;
    private string? _sshRemoteSocket;
    private string? _sshDisplayName;
    private string? _sshXDisplayFile;
    private string? _sshControlPath;
    private string? _forwardTarget;
    private int _attachedTotal;
    private DispatcherTimer? _channelPump;

    private async Task AcceptChannelAsync(BasinCompositorHost host, string endpointText)
    {
        System.Net.EndPoint endpoint;
        if (endpointText.Contains(':', StringComparison.Ordinal))
        {
            var parsed = System.Net.IPEndPoint.Parse(endpointText);
            if (parsed.Address.Equals(System.Net.IPAddress.Any) || parsed.Address.Equals(System.Net.IPAddress.IPv6Any))
            {
                Console.Error.WriteLine("a waypipe channel binds an explicit address, never a wildcard");
                _ = ShutdownAsync(1);
                return;
            }

            endpoint = parsed;
        }
        else
        {
            if (File.Exists(endpointText))
            {
                File.Delete(endpointText);
            }

            endpoint = new System.Net.Sockets.UnixDomainSocketEndPoint(endpointText);
        }

        UpdateStatus($"waiting for a waypipe channel on {endpointText}");
        System.Net.Sockets.Socket listener;
        try
        {
            listener = Listen(endpoint);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"the channel listener failed: {error.Message}");
            _ = ShutdownAsync(1);
            return;
        }

        await AcceptLoopAsync(host, listener);
    }

    private static System.Net.Sockets.Socket Listen(System.Net.EndPoint endpoint)
    {
        var listener = new System.Net.Sockets.Socket(
            endpoint.AddressFamily,
            System.Net.Sockets.SocketType.Stream,
            endpoint is System.Net.Sockets.UnixDomainSocketEndPoint
                ? System.Net.Sockets.ProtocolType.Unspecified
                : System.Net.Sockets.ProtocolType.Tcp);
        listener.Bind(endpoint);
        listener.Listen(8);
        return listener;
    }

    private async Task AcceptLoopAsync(BasinCompositorHost host, System.Net.Sockets.Socket listener)
    {
        _channelListener = listener;
        var channelClients = new HashSet<Wayland.Server.WlClient>();
        try
        {
            var compression = _run!.Compression;
            while (true)
            {
                var accepted = await listener.AcceptAsync();
                var channel = Basin.Transport.Waypipe.WaypipeChannel.AttachChannel(
                    new System.Net.Sockets.NetworkStream(accepted, ownsSocket: true),
                    compression,
                    options: new Basin.Transport.Waypipe.WaypipeChannelOptions
                    {
                        CarriesDmabuf = _run!.Gpu,
                        AcceptsVideo = _run.Video is not null,
                        VideoDecoder = _run.VideoDecoder,
                    });
                _channels.Add(channel);
                var index = ++_attachedTotal;
                channel.Ended += failure =>
                {
                    if (failure is null)
                    {
                        Basin.Diagnostics.BasinLog.Debug($"channel {index} ended");
                        UpdateStatus($"channel {index} ended");
                    }
                    else
                    {
                        Basin.Diagnostics.BasinLog.Warn($"channel {index} ended: {failure.Message}");
                        UpdateStatus($"channel {index} ended: {failure.Message}");
                    }
                };
                var globals = channel.Globals;
                _view!.Post(() =>
                {
                    if (_run!.Gpu && _channelDmabuf is null)
                    {
                        _channelDmabuf = new LinuxDmabufGlobal(
                            host.Display,
                            host.Services.Require<ClientBufferRegistry>(),
                            globals.Formats,
                            Basin.Transport.Waypipe.WaypipeGlobals.SyntheticMainDevice,
                            compositor: host.Services.Require<CompositorGlobal>());
                    }

                    var channelDmabuf = _channelDmabuf;
                    var remote = host.Display.CreateClient(channel.Transport);
                    channelClients.Add(remote);
                    host.Display.SetGlobalFilter((client, wlGlobal, name) =>
                    {
                        var isChannel = channelClients.Contains(client);
                        if (channelDmabuf is not null && wlGlobal is not null && name == "zwp_linux_dmabuf_v1")
                        {
                            return isChannel ? channelDmabuf.Owns(wlGlobal) : !channelDmabuf.Owns(wlGlobal);
                        }

                        return !isChannel || globals.Carries(name);
                    });
                });
                Protocol($"CHANNEL {index} attached");
                UpdateStatus($"{index} channel client(s) attached");
                if (_channelPump is null)
                {
                    _channelPump = new DispatcherTimer(
                        TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, (_, _) => _view?.RequestFrame());
                    _channelPump.Start();
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception error)
        {
            if (!_shuttingDown)
            {
                Console.Error.WriteLine($"the channel listener failed: {error.Message}");
                _ = ShutdownAsync(1);
            }
        }
    }

    private void RemoveRemoteSocket()
    {
        if (_run!.SshHost is not { } sshHost || _sshRemoteSocket is not { } remoteSocket)
        {
            return;
        }

        var info = new ProcessStartInfo("ssh")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        info.ArgumentList.Add("-o");
        info.ArgumentList.Add("BatchMode=yes");
        if (_sshControlPath is { } control)
        {
            info.ArgumentList.Add("-o");
            info.ArgumentList.Add($"ControlPath={control}");
            info.ArgumentList.Add("-o");
            info.ArgumentList.Add("ControlMaster=no");
        }

        info.ArgumentList.Add(sshHost);
        info.ArgumentList.Add(
            $"rm -f {remoteSocket} {_sshXDisplayFile} \"$XDG_RUNTIME_DIR/{_sshDisplayName}\"");
        try
        {
            using var remove = Process.Start(info);
            if (remove is null)
            {
                return;
            }

            var complaint = remove.StandardError.ReadToEndAsync();
            if (remove.WaitForExit(2000) && remove.ExitCode != 0)
            {
                BasinLog.Debug($"{remoteSocket} may still exist on {sshHost}: {complaint.Result.Trim()}");
            }
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            BasinLog.Warn($"{remoteSocket} could not be removed from {sshHost}: {error.Message}");
        }
    }

    [Conditional("DEBUG")]
    private static void Protocol(string line) => Console.WriteLine(line);

    private sealed class Relay(string name)
    {
        private const int Kept = 20;
        private readonly Queue<string> _lines = new(Kept);

        public void Watch(StreamReader reader) => _ = Task.Run(async () =>
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                lock (_lines)
                {
                    if (_lines.Count == Kept)
                    {
                        _lines.Dequeue();
                    }

                    _lines.Enqueue(line);
                }
            }
        });

        public void Report()
        {
            string[] kept;
            lock (_lines)
            {
                kept = [.. _lines];
            }

            foreach (var line in kept)
            {
                BasinLog.Error($"{name}: {line}");
            }
        }
    }

    private async Task LaunchSshAsync(BasinCompositorHost host, string sshHost, string? command)
    {
        if (!StartForward(host, sshHost))
        {
            return;
        }

        if (command is null)
        {
            BasinLog.Info($"holding the channel to {sshHost} open; clients attach as they start");
            UpdateStatus($"connected to {sshHost}, waiting for a client");
            await WatchForwardAsync(sshHost);
            return;
        }

        if (StartRemoteClient(sshHost, command) is { } started)
        {
            _launched.Add(started);
        }

        var attachedTask = Task.Run(async () =>
        {
            while (_channels.Count == 0 && !_shuttingDown)
            {
                await Task.Delay(200);
            }
        });
        var attached = await Task.WhenAny(attachedTask, Task.Delay(TimeSpan.FromSeconds(30))) == attachedTask
            && _channels.Count > 0;
        if (!attached || _ssh!.HasExited)
        {
            if (_ssh!.HasExited)
            {
                BasinLog.Error($"ssh to {sshHost} exited with {_ssh.ExitCode} before the channel arrived");
            }
            else
            {
                BasinLog.Error($"no channel arrived from {sshHost} within 30 seconds");
            }

            _sshRelay?.Report();
            _ = ShutdownAsync(1);
            return;
        }

        await WatchForwardAsync(sshHost);
    }

    private bool StartForward(BasinCompositorHost host, string sshHost)
    {
        _sshRemoteSocket = $"/tmp/waylonia-{Environment.ProcessId}.sock";
        _sshDisplayName = $"waylonia-{Environment.ProcessId}";
        _sshXDisplayFile = $"/tmp/waylonia-x-{Environment.ProcessId}";
        var compress = _run!.Compression switch
        {
            Basin.Transport.Waypipe.WaypipeCompression.None => "none",
            Basin.Transport.Waypipe.WaypipeCompression.Zstd => "zstd",
            _ => "lz4",
        };
        var gpuArgument = _run.Gpu ? string.Empty : "--no-gpu ";
        var videoArgument = _run.Video is { } codec ? $"--video={codec} " : string.Empty;
        var xwayland = _run.XWayland
            ? "if command -v xwayland-satellite >/dev/null 2>&1; then w=--xwls; else w=; fi; "
            : "w=; ";
        var remote =
            $"d=\"$XDG_RUNTIME_DIR/{_sshDisplayName}\"; rm -f \"$d\" {_sshXDisplayFile}; " +
            xwayland +
            $"waypipe --compress {compress} {gpuArgument}{videoArgument}--socket {_sshRemoteSocket} " +
            $"--display {_sshDisplayName} $w server -- " +
            $"sh -c 'printf %s \"$DISPLAY\" > {_sshXDisplayFile}; exec cat >/dev/null'; " +
            $"status=$?; rm -f {_sshRemoteSocket} \"$d\" {_sshXDisplayFile}; exit $status";

        if (_forwardTarget is null)
        {
            if (OperatingSystem.IsWindows())
            {
                System.Net.Sockets.Socket listener;
                try
                {
                    listener = Listen(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"the channel listener failed: {error.Message}");
                    _ = ShutdownAsync(1);
                    return false;
                }

                _forwardTarget = $"127.0.0.1:{((System.Net.IPEndPoint)listener.LocalEndPoint!).Port}";
                UpdateStatus($"waiting for a waypipe channel on {_forwardTarget}");
                _ = AcceptLoopAsync(host, listener);
            }
            else
            {
                var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? Path.GetTempPath();
                _forwardTarget = Path.Combine(runtimeDir, $"waylonia-ssh-{Environment.ProcessId}.sock");
                _ = AcceptChannelAsync(host, _forwardTarget);
            }
        }

        var info = new ProcessStartInfo("ssh")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
        };
        info.ArgumentList.Add("-o");
        info.ArgumentList.Add("BatchMode=yes");
        info.ArgumentList.Add("-o");
        info.ArgumentList.Add("StreamLocalBindUnlink=yes");
        info.ArgumentList.Add("-o");
        info.ArgumentList.Add("ExitOnForwardFailure=yes");
        if (!OperatingSystem.IsWindows())
        {
            var controlDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? Path.GetTempPath();
            _sshControlPath = Path.Combine(controlDir, $"waylonia-ssh-{Environment.ProcessId}.ctl");
            try
            {
                File.Delete(_sshControlPath);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }

            info.ArgumentList.Add("-o");
            info.ArgumentList.Add("ControlMaster=auto");
            info.ArgumentList.Add("-o");
            info.ArgumentList.Add($"ControlPath={_sshControlPath}");
            info.ArgumentList.Add("-o");
            info.ArgumentList.Add("ControlPersist=no");
        }

        info.ArgumentList.Add("-R");
        info.ArgumentList.Add($"{_sshRemoteSocket}:{_forwardTarget}");
        info.ArgumentList.Add(sshHost);
        info.ArgumentList.Add(remote);
        try
        {
            _ssh = Process.Start(info);
        }
        catch (Exception error)
        {
            BasinLog.Error($"ssh could not start: {error.Message}");
            _ = ShutdownAsync(1);
            return false;
        }

        if (_ssh is null)
        {
            BasinLog.Error($"ssh could not start");
            _ = ShutdownAsync(1);
            return false;
        }

        _sshRelay = new Relay("ssh");
        _sshRelay.Watch(_ssh.StandardOutput);
        _sshRelay.Watch(_ssh.StandardError);
        return true;
    }

    private async Task WatchForwardAsync(string sshHost)
    {
        var ssh = _ssh!;
        await ssh.WaitForExitAsync();
        if (_shuttingDown || !ReferenceEquals(ssh, _ssh))
        {
            return;
        }

        if (_attachedTotal > 0)
        {
            BasinLog.Info($"the connection to {sshHost} ended; a hotkey opens it again");
            UpdateStatus($"disconnected from {sshHost}");
            return;
        }

        BasinLog.Error($"ssh to {sshHost} exited with {ssh.ExitCode}");
        _sshRelay?.Report();
        _ = ShutdownAsync(ssh.ExitCode == 0 ? 0 : 1);
    }

    private async Task WatchClientAsync(Process client, Relay relay, string command)
    {
        await client.WaitForExitAsync();
        if (_shuttingDown || client.ExitCode == 0)
        {
            return;
        }

        BasinLog.Warn($"'{command}' exited with {client.ExitCode}");
        relay.Report();
    }

    private static void WriteScreenshot(BasinCompositorHost host, string path)
    {
        using var renderer = new Basin.Render.Skia.SkiaRenderer();
        var view = host.Session.Outputs.Count > 0 ? host.Session.Outputs[0] : null;
        var width = view?.Output.CurrentMode.Width ?? 1024;
        var height = view?.Output.CurrentMode.Height ?? 768;
        var shot = new MemoryBuffer(width, height, DrmFormat.Xrgb8888);
        var origin = view?.Position ?? default;
        host.Scene.Root.SetPosition(-origin.X, -origin.Y);
        try
        {
            host.Scene.Render(renderer, shot, new RenderColor(0.06f, 0.06f, 0.08f, 1f));
        }
        finally
        {
            host.Scene.Root.SetPosition(0, 0);
        }

        BufferCapture.WritePng(shot, path);
        shot.Destroy();
        Console.WriteLine($"SCREENSHOT {path}");
    }

    private async Task ShutdownAsync(int status)
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        _exitStatus = status;
        _globalHotkeys?.Dispose();
        _globalHotkeys = null;
        HostCursor.Close();

        if (_run!.Screenshot is { } path && _host is { } aliveHost && _view is { } pump)
        {
            var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pump.Post(() =>
            {
                WriteScreenshot(aliveHost, path);
                written.TrySetResult();
            });
            pump.RequestFrame();
            await Task.WhenAny(written.Task, Task.Delay(2000));
        }

        var stopped = _launched.Count > 0;
        foreach (var launched in _launched)
        {
            BasinDiagnostics.StopClient(launched);
        }

        _launched.Clear();
        if (_client is { } client)
        {
            BasinDiagnostics.StopClient(client);
            stopped = true;
        }

        if (stopped)
        {
            for (var i = 0; i < 20 && _host is { Display.Clients.Count: > 0 }; i++)
            {
                _view?.RequestFrame();
                await Task.Delay(50);
            }
        }

        _channelPump?.Stop();
        if (_ssh is { } ssh)
        {
            RemoveRemoteSocket();
            if (!ssh.HasExited)
            {
                try
                {
                    ssh.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        if (_sshControlPath is { } control)
        {
            try
            {
                File.Delete(control);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }

        _channelListener?.Dispose();
        foreach (var channel in _channels)
        {
            channel.Dispose();
        }

        if (_view is { } integrationPump)
        {
            if (_channelDmabuf is { } channelDmabuf)
            {
                integrationPump.Post(channelDmabuf.Dispose);
            }

            if (_clipboard is { } clipboard)
            {
                integrationPump.Post(clipboard.Dispose);
            }

            if (_hostDrag is { } hostDrag)
            {
                integrationPump.Post(hostDrag.Dispose);
            }
        }

        if (_windows is { } windows)
        {
            await windows.CloseAllAsync();
        }

        if (_view is { } view)
        {
            await view.ShutdownAsync();
        }

        Protocol(
            $"FRAMES {Rendered} LIVE {(BasinCounters.Enabled ? BasinCounters.LiveObjects.ToString() : "untracked")}");
        if (BasinCounters.Enabled && (BasinCounters.LiveObjects != 0 || BasinCounters.PendingFrees != 0))
        {
            Console.Error.WriteLine(
                $"teardown not clean (live={BasinCounters.LiveObjects} pendingFrees={BasinCounters.PendingFrees})");
            Console.Error.WriteLine(BasinCounters.CensusReport());
            _exitStatus = 1;
        }

        _window?.Close();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
