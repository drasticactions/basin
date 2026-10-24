using Avalonia;
using Basin;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Basin.Avalonia;
using Basin.Hosted;
using Basin.Transport.Waypipe;

namespace PortComp;

public sealed class MainView : UserControl
{
    private readonly BasinOutputView _output;
    private readonly Panel _stage = new();
    private readonly TextBox _endpoint;
    private readonly Button _connect;
    private readonly TextBlock _status;
    private readonly AvaloniaTextInput _textInput;
    private BasinCompositorHost? _host;
    private TopLevel? _top;
    private BasinToplevelView? _shellView;
    private PortCompShell? _shell;
    private readonly ComboBox _mode;
    private readonly SelectableTextBlock _hint;
    private PortCompSession? _session;
    private WaypipeListenSession? _listening;
    private bool _connecting;
    private bool _shutdown;

    public MainView()
    {
        _textInput = new AvaloniaTextInput(action => _output!.Post(action));
        _output = new BasinOutputView(CreateHost, createOwnView: false) { IsHitTestVisible = false };
        _output.HostReady += OnHostReady;
        _output.HostFailed += error => Status($"the compositor could not start: {error.Message}");

        _endpoint = new TextBox { Text = PortCompLink.DefaultEndpoint, PlaceholderText = "host:port or ws://…", MinWidth = 240 };
        _connect = new Button { Content = "Connect", IsEnabled = false };
        _connect.Click += (_, _) => _ = ToggleAsync();
        _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) };
        _hint = new SelectableTextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0), IsVisible = false };
        _mode = new ComboBox { ItemsSource = new[] { "Dial", "Listen" }, SelectedIndex = 0, IsEnabled = PortCompLink.CanListen, Margin = new Thickness(0, 0, 8, 0) };
        _mode.SelectionChanged += (_, _) => OnModeChanged();

        var bar = new DockPanel { Margin = new Thickness(8, 4), LastChildFill = true };
        DockPanel.SetDock(_mode, Dock.Left);
        DockPanel.SetDock(_connect, Dock.Right);
        DockPanel.SetDock(_status, Dock.Right);
        DockPanel.SetDock(_hint, Dock.Right);
        bar.Children.Add(_mode);
        bar.Children.Add(_connect);
        bar.Children.Add(_status);
        bar.Children.Add(_hint);
        bar.Children.Add(_endpoint);

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        _stage.Children.Add(_output);
        root.Children.Add(bar);
        root.Children.Add(_stage);
        Content = root;
        Background = global::Avalonia.Media.Brushes.Black;
        _stage.SizeChanged += (_, _) => FollowStage();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is { } top)
        {
            _top = top;
            top.ScalingChanged += OnScalingChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_top is { } top)
        {
            top.ScalingChanged -= OnScalingChanged;
            _top = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnScalingChanged(object? sender, EventArgs e) => FollowStage();

    private void FollowStage()
    {
        if (_shell is not { } shell)
        {
            return;
        }

        var (width, height, scale) = StageSize();
        _output.Post(() => shell.Resize(width, height, scale));
    }

    private (int Width, int Height, double Scale) StageSize()
    {
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling is > 0 and var known ? known : 1.0;
        var width = Math.Max(1, (int)Math.Round(Math.Max(_stage.Bounds.Width, 1) * scale));
        var height = Math.Max(1, (int)Math.Round(Math.Max(_stage.Bounds.Height, 1) * scale));
        return (width, height, scale);
    }

    public void Suspend() => _output.Post(() => _host?.Suspend());

    public void Resume() => _output.Post(() => _host?.Resume());

    public string Endpoint
    {
        get => _endpoint.Text ?? string.Empty;
        set => _endpoint.Text = value;
    }

    public string StatusText => _status.Text ?? string.Empty;

    public bool Connected => _session is not null || _listening is not null;

    public bool Listening
    {
        get => _mode.SelectedIndex == 1;
        set => _mode.SelectedIndex = value ? 1 : 0;
    }

    public WaypipeListenSession? ListenSession => _listening;

    private void OnModeChanged()
    {
        if (_session is not null || _listening is not null)
        {
            return;
        }

        var listening = Listening;
        if (_endpoint.Text is null or "" || _endpoint.Text == PortCompLink.DefaultEndpoint || _endpoint.Text == PortCompLink.DefaultListenEndpoint)
        {
            _endpoint.Text = listening ? PortCompLink.DefaultListenEndpoint : PortCompLink.DefaultEndpoint;
        }

        _connect.Content = listening ? "Listen" : "Connect";
    }

    public Task ToggleAsync() => ToggleCoreAsync();

    public async Task ShutdownAsync()
    {
        _shutdown = true;
        _session?.Dispose();
        _session = null;
        _listening?.Dispose();
        _listening = null;
        _host = null;
        var shell = _shell;
        _shell = null;
        if (shell is not null)
        {
            _output.Post(shell.Dispose);
        }

        if (_shellView is { } view)
        {
            _shellView = null;
            await view.ShutdownAsync();
        }

        await _output.ShutdownAsync();
    }

    public PortCompShell? Shell => _shell;

    public BasinCompositorHost? Host => _host;

    private BasinCompositorHost CreateHost() =>
        new(new BasinCompositorOptions
        {
            AppName = "port-comp",
            ManagedTransport = true,
            TextInput = _textInput,
            ConfigureServices = (_, services) => services
                .Without("zwlr_layer_shell_v1")
                .Without("ext_session_lock_manager_v1"),
        });

    private void OnHostReady(BasinCompositorHost host)
    {
        if (_shutdown)
        {
            return;
        }

        _host = host;
        _shellView = new BasinToplevelView(host, CreateShellView);
        _stage.Children.Add(_shellView);
        _textInput.AttachView(_shellView);
        _shellView.Focus();
        _connect.IsEnabled = true;
        Status("ready");
        if (PortCompApp.StartupListen)
        {
            Listening = true;
        }

        if (PortCompApp.StartupEndpoint is { Length: > 0 } startup)
        {
            Endpoint = startup;
            if (PortCompApp.AutoConnect)
            {
                _ = ToggleAsync();
            }
        }
    }

    private BasinViewOutput CreateShellView(BasinCompositorHost host)
    {
        var (width, height, scale) = StageSize();
        var shell = new PortCompShell(host, width, height, scale, action => Dispatcher.UIThread.Post(action), action => _output.Post(action));
        _shell = shell;
        Dispatcher.UIThread.Post(() =>
        {
            if (_shellView is { } view)
            {
                view.InputSink = shell.HandleInput;
            }
        });
        return shell.View;
    }

    private async Task ToggleCoreAsync()
    {
        if (_host is not { } host || _connecting)
        {
            return;
        }

        if (_session is { } open)
        {
            _session = null;
            open.Dispose();
            _connect.Content = "Connect";
            Status("disconnected");
            return;
        }

        if (_listening is { } listening)
        {
            _listening = null;
            listening.Dispose();
            _connect.Content = "Listen";
            _mode.IsEnabled = PortCompLink.CanListen;
            _hint.IsVisible = false;
            Status("stopped listening");
            return;
        }

        if (Listening)
        {
            await ListenAsync(host);
            return;
        }

        _connecting = true;
        _connect.IsEnabled = false;
        var endpoint = _endpoint.Text ?? string.Empty;
        Status($"connecting to {endpoint}");
        try
        {
            var session = new PortCompSession(host, _output.Post, token => WaypipeEndpoint.OpenAsync(endpoint, token), PortCompShell.ChannelOptions());
            session.Ended += failure => Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(_session, session))
                {
                    _session = null;
                    _connect.Content = "Connect";
                }

                Status(failure is null ? "the session ended" : $"the session ended: {failure.Message}");
            });
            session.Changed += () => Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(_session, session))
                {
                    Status($"connected to {endpoint}, {session.Clients} client(s)");
                }
            });
            _session = session;
            try
            {
                await session.StartAsync();
            }
            catch
            {
                _session = null;
                session.Dispose();
                throw;
            }

            _connect.Content = "Disconnect";
            Status($"connected to {endpoint}");
        }
        catch (Exception error)
        {
            Status($"could not connect: {error.Message}");
        }
        finally
        {
            _connecting = false;
            _connect.IsEnabled = true;
        }
    }

    private async Task ListenAsync(BasinCompositorHost host)
    {
        _connecting = true;
        _connect.IsEnabled = false;
        _mode.IsEnabled = false;
        var endpoint = (_endpoint.Text ?? string.Empty).Trim();
        Status($"binding {endpoint}");
        try
        {
            var vm = PortCompApp.StartupHyperVVm is { Length: > 0 } word ? HyperVVm.Parse(word) : (HyperVVm?)null;
            var session = await WaypipeListenSession.StartAsync(
                host.Display,
                host.Services.Require<ClientBufferRegistry>(),
                host.Services.Require<CompositorGlobal>(),
                () => host.Dmabuf is not null,
                host.Loop,
                _output.Post,
                endpoint,
                vm,
                PortCompShell.ChannelOptions());
            session.Changed += () => Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(_listening, session))
                {
                    Status($"{endpoint}, {session.Clients} client(s)");
                }
            });
            session.Rebound += id => Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(_listening, session))
                {
                    Status($"rebound on WSL VM {id}");
                }
            });
            session.Ended += failure => Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(_listening, session))
                {
                    _listening = null;
                    session.Dispose();
                    _connect.Content = "Listen";
                    _mode.IsEnabled = PortCompLink.CanListen;
                    _hint.IsVisible = false;
                }

                Status(failure is null ? "the listener stopped" : $"the listener stopped: {failure.Message}");
            });
            _listening = session;
            _connect.Content = "Stop";
            Status(session.VmLabel is { } label ? $"listening on {endpoint} ({label})" : $"listening on {session.Listener.LocalEndPoint}");
            if (PortCompLink.GuestCommand(endpoint) is { } command)
            {
                _hint.Text = command;
                _hint.IsVisible = true;
            }
        }
        catch (Exception error)
        {
            _mode.IsEnabled = PortCompLink.CanListen;
            Status(error.Message);
        }
        finally
        {
            _connecting = false;
            _connect.IsEnabled = true;
        }
    }

    private void Status(string text)
    {
        _status.Text = text;
        Console.WriteLine($"port-comp: {text}");
    }
}
