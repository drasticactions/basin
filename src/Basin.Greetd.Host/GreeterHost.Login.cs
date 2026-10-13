using Basin.Diagnostics;

namespace Basin.Greetd.Host;

public sealed partial class GreeterHost
{
    private GreetdClient? _client;
    private GreeterFlow? _flow;
    private LoopbackGreetd? _demo;

    public GreeterFlow Flow => _flow ?? throw new InvalidOperationException("the login has not started");

    public GreeterFlow? FlowOrNull => _flow;

    public event Action<GreeterStep>? Stepped;

    public static string AuthWord(GreetdAuthMessageType type) => type switch
    {
        GreetdAuthMessageType.Visible => "visible",
        GreetdAuthMessageType.Secret => "secret",
        GreetdAuthMessageType.Info => "info",
        _ => "error",
    };

    public GreeterFlow StartLogin()
    {
        if (_flow is not null)
        {
            return _flow;
        }

        if (_options.Demo)
        {
            var (server, socket) = _options.DemoOptions is { } demo ? LoopbackGreetd.Create(_host.Loop, demo) : LoopbackGreetd.Create(_host.Loop);
            _demo = server;
            _demo.SessionStarted += start => _log.Info($"demo: start_session {string.Join(' ', start.Cmd)}");
            _client = GreetdClient.Adopt(_host.Loop, socket);
            BasinReport.Line("GREETD demo");
        }
        else
        {
            _client = GreetdClient.Connect(_host.Loop, _greetdPath!);
            BasinReport.Line("GREETD connected");
        }

        _flow = new GreeterFlow(_host.Loop, _client, _options.Settings, _options.Memory, _seatState.Keyboard, _options.FlowOptions);
        _flow.Stepped += OnStep;
        _flow.Closed += reason =>
        {
            _log.Error($"greetd: {reason}");
            Stop("greetd-closed", 1);
        };
        return _flow;
    }

    private static string? GreetdPath(GreeterHostOptions options)
    {
        var fromEnvironment = GreetdClient.SocketFromEnvironment();
        if (options.Demo)
        {
            if (fromEnvironment is not null)
            {
                throw new InvalidOperationException($"--demo is refused under greetd ({GreetdClient.SocketVariable} is set)");
            }

            return null;
        }

        return options.GreetdSocket ?? fromEnvironment
            ?? throw new InvalidOperationException($"not under greetd: {GreetdClient.SocketVariable} is not set and --greetd-sock names nothing; --demo runs without greetd");
    }

    private void OnStep(GreeterStep step)
    {
        switch (step.Kind)
        {
            case GreeterStepKind.UsersRead:
                Stepped?.Invoke(step);
                BasinReport.Line($"USERS {step.Count}");
                return;
            case GreeterStepKind.SessionsRead:
                Stepped?.Invoke(step);
                BasinReport.Line($"SESSIONS {step.Count}");
                return;
            case GreeterStepKind.Message:
            case GreeterStepKind.Prompt:
                BasinReport.Line($"AUTH {AuthWord(step.PromptType)} {step.Text}");
                break;
            case GreeterStepKind.LoginOk:
                BasinReport.Line("LOGIN ok");
                break;
            case GreeterStepKind.AuthFailed:
                BasinReport.Line("LOGIN failed auth_error");
                break;
            case GreeterStepKind.Failed:
                BasinReport.Line($"LOGIN failed error {step.Text}");
                break;
            case GreeterStepKind.Started:
                BasinReport.Line($"START session={step.Text}");
                Stepped?.Invoke(step);
                _reader?.Stop();
                Stop("start");
                return;
            case GreeterStepKind.LayoutApplied:
                Stepped?.Invoke(step);
                BasinReport.Line($"LAYOUT {step.Text}");
                return;
            case GreeterStepKind.LayoutFailed:
                Stepped?.Invoke(step);
                BasinReport.Line($"LAYOUT {step.Text} failed");
                return;
        }

        Stepped?.Invoke(step);
    }
}
