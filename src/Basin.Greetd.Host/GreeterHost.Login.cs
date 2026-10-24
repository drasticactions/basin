using Basin.Diagnostics;
using Basin.Host;

namespace Basin.Greetd.Host;

/// <content>
/// The greetd connection and the login flow of the greeter host.
/// </content>
public sealed partial class GreeterHost
{
    private GreetdClient? _client;
    private GreeterFlow? _flow;
    private LoopbackGreetd? _demo;

    /// <summary>
    /// Gets the login flow.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="StartLogin"/> has not run.</exception>
    public GreeterFlow Flow => _flow ?? throw new InvalidOperationException("the login has not started");

    /// <summary>
    /// Gets the login flow, or <see langword="null"/> before <see cref="StartLogin"/>.
    /// </summary>
    public GreeterFlow? FlowOrNull => _flow;

    /// <summary>
    /// Occurs for each step of the flow, after the host prints its line for the step.
    /// </summary>
    public event Action<GreeterStep>? Stepped;

    /// <summary>
    /// Gives the wire word for an auth message type, as the <c>AUTH</c> line prints it.
    /// </summary>
    /// <param name="type">The auth message type.</param>
    /// <returns><c>visible</c>, <c>secret</c>, <c>info</c> or <c>error</c>.</returns>
    public static string AuthWord(GreetdAuthMessageType type) => type switch
    {
        GreetdAuthMessageType.Visible => "visible",
        GreetdAuthMessageType.Secret => "secret",
        GreetdAuthMessageType.Info => "info",
        _ => "error",
    };

    /// <summary>
    /// Connects to greetd, or starts the demo server, and creates the flow. A second call returns the same flow. The program calls <see cref="GreeterFlow.Start"/> itself.
    /// </summary>
    /// <returns>The flow. The host owns it.</returns>
    /// <exception cref="System.Net.Sockets.SocketException">The greetd socket does not accept the connection.</exception>
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

        var flowOptions = _host.Handoff.StampsSession
            ? _options.FlowOptions with { Environment = [.. _options.FlowOptions.Environment, $"{HandoffChoice.Variable}=retain"] }
            : _options.FlowOptions;
        _flow = new GreeterFlow(_host.Loop, _client, _options.Settings, _options.Memory, _seatState.Keyboard, flowOptions);
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
                StopWhenSettled();
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

    private void StopWhenSettled()
    {
        if (!_host.Handoff.RetainsOn(HandoffStop.SessionStart) || _settle is not { } settle)
        {
            Stop("start");
            return;
        }

        IEventSource? limit = null;
        Action? settled = null;
        void Finish()
        {
            if (settled is null)
            {
                return;
            }

            settle.Settled -= settled;
            settled = null;
            limit?.Remove();
            Stop("start");
        }

        settled = Finish;
        settle.Settled += settled;
        limit = _host.Loop.AddTimer(Finish);
        limit.UpdateTimer(400);
        _outputs.ScheduleAll();
    }
}
