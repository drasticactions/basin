using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Basin.Avalonia;
using Basin.Hosted;

namespace Tarn;

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
    private TarnShell? _shell;
    private TarnSession? _session;
    private bool _connecting;
    private bool _shutdown;

    public MainView()
    {
        _textInput = new AvaloniaTextInput(action => _output!.Post(action));
        _output = new BasinOutputView(CreateHost, createOwnView: false) { IsHitTestVisible = false };
        _output.HostReady += OnHostReady;
        _output.HostFailed += error => Status($"the compositor could not start: {error.Message}");

        _endpoint = new TextBox { Text = TarnLink.DefaultEndpoint, PlaceholderText = "host:port or ws://…", MinWidth = 240 };
        _connect = new Button { Content = "Connect", IsEnabled = false };
        _connect.Click += (_, _) => _ = ToggleAsync();
        _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) };

        var bar = new DockPanel { Margin = new Thickness(8, 4), LastChildFill = true };
        DockPanel.SetDock(_connect, Dock.Right);
        DockPanel.SetDock(_status, Dock.Right);
        bar.Children.Add(_connect);
        bar.Children.Add(_status);
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

    public bool Connected => _session is not null;

    public Task ToggleAsync() => ToggleCoreAsync();

    public async Task ShutdownAsync()
    {
        _shutdown = true;
        _session?.Dispose();
        _session = null;
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

    public TarnShell? Shell => _shell;

    public BasinCompositorHost? Host => _host;

    private BasinCompositorHost CreateHost() =>
        new(new BasinCompositorOptions { AppName = "tarn", TextInput = _textInput });

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
        if (TarnApp.StartupEndpoint is { Length: > 0 } startup)
        {
            Endpoint = startup;
            if (TarnApp.AutoConnect)
            {
                _ = ToggleAsync();
            }
        }
    }

    private BasinViewOutput CreateShellView(BasinCompositorHost host)
    {
        var (width, height, scale) = StageSize();
        var shell = new TarnShell(host, width, height, scale, action => Dispatcher.UIThread.Post(action), action => _output.Post(action));
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

        _connecting = true;
        _connect.IsEnabled = false;
        var endpoint = _endpoint.Text ?? string.Empty;
        Status($"connecting to {endpoint}");
        try
        {
            var session = new TarnSession(host, _output.Post, token => TarnLink.OpenAsync(endpoint, token), TarnShell.ChannelOptions());
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

    private void Status(string text)
    {
        _status.Text = text;
        Console.WriteLine($"tarn: {text}");
    }
}
