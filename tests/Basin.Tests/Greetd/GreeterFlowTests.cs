using Basin.Config;
using Basin.Diagnostics;
using Basin.Greetd;
using Tomlyn;
using Xunit;

namespace Basin.Tests;

public sealed class GreeterFlowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "basin-greeter-flow-" + Guid.NewGuid().ToString("N"));

    public GreeterFlowTests()
    {
        Write("etc/passwd", string.Join('\n',
            "root:x:0:0:root:/root:/bin/bash",
            "ada:x:1000:1000:Ada Lovelace:/home/ada:/bin/bash",
            "grace:x:1001:1001:Grace Hopper:/home/grace:/bin/bash",
            ""));
        Write("etc/login.defs", "UID_MIN 1000\nUID_MAX 60000\n");
        Write("usr/share/wayland-sessions/sway.desktop", "[Desktop Entry]\nType=Application\nName=Sway\nExec=sway\n");
        Write("usr/share/wayland-sessions/river.desktop", "[Desktop Entry]\nType=Application\nName=River\nExec=river\n");
        Write("environments", "bash\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private GreeterSettings Settings(string extra = "") => GreeterSettings.Read(new TomlReader(Toml.ToModel($"""
        [sessions]
        environments = "{Path.Combine(_root, "environments")}"
        default = "sway"

        [keyboard]
        layouts = ["us", "de", "zz-nope"]
        {extra}
        """), BasinLog.For("test")));

    private sealed class Rig : IDisposable
    {
        public Rig(string root, GreeterSettings settings, IReadOnlyList<string>? environment = null)
        {
            Host = new CompositorTestHost();
            Greetd = new FakeGreetd();
            Client = GreetdClient.Connect(Host.Loop, Greetd.SocketPath);
            Memory = GreeterMemory.Load(Path.Combine(root, "state.toml"), BasinLog.For("test"));
            Flow = new GreeterFlow(Host.Loop, Client, settings, Memory, Host.Seat.Keyboard, new GreeterFlowOptions
            {
                Root = root,
                Environment = environment ?? [],
                Homed = false,
            });
            Flow.Stepped += Steps.Add;
            Flow.Closed += reason => ClosedReason = reason;
        }

        public CompositorTestHost Host { get; }

        public FakeGreetd Greetd { get; }

        public GreetdClient Client { get; }

        public GreeterMemory Memory { get; }

        public GreeterFlow Flow { get; }

        public List<GreeterStep> Steps { get; } = [];

        public string? ClosedReason { get; private set; }

        public void PumpUntil(Func<bool> condition) => PortalBusTests.PumpUntil(Host, condition);

        public void PumpUntilStep(GreeterStepKind kind, int after = 0) => PumpUntil(() => Steps.Skip(after).Any(step => step.Kind == kind));

        public void Dispose()
        {
            Flow.Dispose();
            Client.Dispose();
            Greetd.Dispose();
            Host.Dispose();
        }
    }

    [Fact]
    public void A_login_starts_the_chosen_session_with_the_layout_and_remembers_both()
    {
        using var rig = new Rig(_root, Settings(), ["EXTRA=1"]);
        rig.Greetd.Respond(
            new GreetdAuthMessage(GreetdAuthMessageType.Info, "Welcome"),
            new GreetdAuthMessage(GreetdAuthMessageType.Secret, "Password:"),
            new GreetdSuccess(),
            new GreetdSuccess());
        rig.Flow.Start();
        rig.Flow.ShowUsers();
        Assert.Equal(["ada", "grace"], rig.Flow.Users.Select(user => user.Name));
        Assert.Equal(3, rig.Steps.Single(step => step.Kind == GreeterStepKind.SessionsRead).Count);
        Assert.Equal("sway", rig.Flow.SelectedSession);

        Assert.True(rig.Flow.SelectLayout("de", byUser: true));
        Assert.True(rig.Flow.SelectSession("river", byUser: true));
        Assert.True(rig.Flow.Choose("ada"));
        Assert.Equal(GreeterPhase.Conversation, rig.Flow.Phase);
        Assert.Equal("Ada Lovelace", rig.Flow.CurrentDisplayName);
        Assert.Contains(rig.Steps, step => step.Kind == GreeterStepKind.Prompt && step.PromptType == GreetdAuthMessageType.Secret);
        Assert.True(rig.Flow.PromptSecret);
        Assert.Equal("Password: ", rig.Flow.PromptText);
        Assert.Empty(rig.Greetd.Requests());

        Assert.True(rig.Flow.Answer("hunter2"));
        rig.PumpUntilStep(GreeterStepKind.Started);
        Assert.Equal(["Welcome"], rig.Flow.Messages);
        Assert.Equal(new GreetdCreateSession("ada"), rig.Greetd.Requests()[0]);

        Assert.Equal(GreeterPhase.Started, rig.Flow.Phase);
        var start = Assert.IsType<GreetdStartSession>(rig.Greetd.Requests()[^1]);
        Assert.Contains("river", start.Cmd);
        Assert.Contains("XKB_DEFAULT_LAYOUT=de", start.Env);
        Assert.Contains("EXTRA=1", start.Env);
        Assert.Equal(new GreetdPostAuthMessageResponse("hunter2"), rig.Greetd.Requests()[2]);
        var memory = GreeterMemory.Load(Path.Combine(_root, "state.toml"), BasinLog.For("test"));
        Assert.Equal("ada", memory.LastUser);
        Assert.Equal("river", memory.SessionOf("ada"));
        Assert.Equal("de", memory.LayoutOf("ada"));
        Assert.Contains(rig.Steps, step => step.Kind == GreeterStepKind.LoginOk);
    }

    [Fact]
    public void An_auth_error_asks_again_without_opening_a_conversation_until_the_next_answer()
    {
        using var rig = new Rig(_root, Settings());
        rig.Greetd.Respond(
            new GreetdAuthMessage(GreetdAuthMessageType.Secret, "Password:"),
            new GreetdError(GreetdErrorType.AuthError, "Authentication failure"),
            new GreetdSuccess(),
            new GreetdAuthMessage(GreetdAuthMessageType.Secret, "Password:"),
            new GreetdSuccess(),
            new GreetdSuccess());
        rig.Flow.Start();
        rig.Flow.ShowUsers();
        rig.Flow.EnterName("  grace ");
        Assert.True(rig.Flow.Answer("wrong"));
        rig.PumpUntilStep(GreeterStepKind.AuthFailed);
        var failed = rig.Steps.Count;
        rig.PumpUntilStep(GreeterStepKind.Prompt, failed - 1);

        Assert.Equal(GreeterPhase.Conversation, rig.Flow.Phase);
        Assert.Equal("Login incorrect", rig.Flow.Error);
        Assert.False(rig.Flow.ErrorNeedsDismiss);
        Assert.Equal("grace", rig.Flow.CurrentUser);
        Assert.Equal("Password: ", rig.Flow.PromptText);
        Assert.Equal([new GreetdCreateSession("grace")], rig.Greetd.Requests().OfType<GreetdCreateSession>());
        Assert.Equal(new GreetdPostAuthMessageResponse("wrong"), rig.Greetd.Requests()[1]);

        Assert.True(rig.Flow.Answer("right"));
        rig.PumpUntilStep(GreeterStepKind.Started);
        var requests = rig.Greetd.Requests();
        Assert.Equal(2, requests.OfType<GreetdCreateSession>().Count());
        Assert.Contains(new GreetdPostAuthMessageResponse("right"), requests);
    }

    [Fact]
    public void An_error_needs_a_dismiss_that_returns_to_the_users()
    {
        using var rig = new Rig(_root, Settings());
        rig.Greetd.Respond(new GreetdError(GreetdErrorType.Error, "account expired"), new GreetdSuccess());
        rig.Flow.Start();
        rig.Flow.ShowUsers();
        rig.Flow.Choose("ada");
        rig.Flow.Answer("pw");
        rig.PumpUntilStep(GreeterStepKind.Failed);

        Assert.Equal(GreeterPhase.Error, rig.Flow.Phase);
        Assert.Equal("account expired", rig.Flow.Error);
        Assert.True(rig.Flow.ErrorNeedsDismiss);

        rig.Flow.Dismiss();
        Assert.Equal(GreeterPhase.Users, rig.Flow.Phase);
        Assert.Null(rig.Flow.Error);
    }

    [Fact]
    public void Cancel_before_an_answer_sends_nothing_to_greetd()
    {
        using var rig = new Rig(_root, Settings());
        rig.Flow.Start();
        rig.Flow.ShowUsers();
        rig.Flow.Choose("ada");
        rig.Flow.Cancel();
        rig.Flow.Choose("grace");
        rig.Flow.ChooseOther();
        rig.Flow.EnterName("ada");
        rig.Flow.Cancel();

        Assert.Equal(GreeterPhase.Users, rig.Flow.Phase);
        Assert.Empty(rig.Greetd.Requests());
        Assert.False(rig.Flow.Answer("late"));
    }

    [Fact]
    public void Cancel_in_a_conversation_sends_cancel_session_and_shows_the_users()
    {
        using var rig = new Rig(_root, Settings());
        rig.Greetd.Respond(new GreetdAuthMessage(GreetdAuthMessageType.Visible, "Token:"), new GreetdSuccess());
        rig.Flow.Start();
        rig.Flow.ShowUsers();
        rig.Flow.Choose("ada");
        rig.Flow.Answer("pw");
        rig.PumpUntil(() => rig.Flow.PromptText == "Token:");
        Assert.False(rig.Flow.PromptSecret);

        rig.Flow.Cancel();
        rig.PumpUntil(() => rig.Flow.LoginState == GreetdLoginState.Idle);

        Assert.Equal(GreeterPhase.Users, rig.Flow.Phase);
        Assert.IsType<GreetdCancelSession>(rig.Greetd.Requests()[^1]);
        Assert.False(rig.Flow.Answer("late"));
    }

    [Fact]
    public void Choosing_another_user_at_the_prompt_sends_nothing_and_answers_for_them()
    {
        using var rig = new Rig(_root, Settings());
        rig.Greetd.Respond(new GreetdAuthMessage(GreetdAuthMessageType.Secret, "Password:"), new GreetdSuccess(), new GreetdSuccess());
        rig.Flow.Start();
        rig.Flow.ShowUsers();
        rig.Flow.Choose("ada");
        var steps = rig.Steps.Count;

        Assert.True(rig.Flow.Choose("grace"));
        Assert.Contains(rig.Steps.Skip(steps), step => step.Kind == GreeterStepKind.Prompt);
        Assert.Equal("grace", rig.Flow.CurrentUser);
        Assert.Equal(GreeterPhase.Conversation, rig.Flow.Phase);
        Assert.Empty(rig.Greetd.Requests());

        rig.Flow.Answer("pw");
        rig.PumpUntilStep(GreeterStepKind.Started);
        var requests = rig.Greetd.Requests();
        Assert.Equal(new GreetdCreateSession("grace"), requests[0]);
        Assert.Equal(new GreetdPostAuthMessageResponse("pw"), requests[1]);
        Assert.DoesNotContain(requests, request => request is GreetdCancelSession);
    }

    [Fact]
    public void The_remembered_user_brings_back_their_session_and_layout()
    {
        Write("state.toml", "last_user = \"grace\"\n\n[users.grace]\nsession = \"river\"\nlayout = \"de\"\n");
        using var rig = new Rig(_root, Settings());
        rig.Flow.Start();
        rig.Flow.ShowUsers();

        Assert.Equal("grace", rig.Flow.SuggestedUser);
        Assert.Equal("river", rig.Flow.SelectedSession);
        Assert.Equal("de", rig.Flow.SelectedLayout);
        Assert.Contains(rig.Steps, step => step.Kind == GreeterStepKind.UsersShown && step.Text == "grace");
        Assert.Contains(rig.Steps, step => step.Kind == GreeterStepKind.LayoutApplied && step.Text == "de");
    }

    [Fact]
    public void A_session_the_user_picked_survives_choosing_a_user_with_another_remembered()
    {
        Write("state.toml", "last_user = \"ada\"\n\n[users.grace]\nsession = \"river\"\n");
        using var rig = new Rig(_root, Settings());
        rig.Greetd.Respond(new GreetdAuthMessage(GreetdAuthMessageType.Secret, "Password:"));
        rig.Flow.Start();
        rig.Flow.ShowUsers();
        Assert.Equal("sway", rig.Flow.SelectedSession);

        Assert.True(rig.Flow.SelectSession("river", byUser: false));
        Assert.Equal("river", rig.Flow.SelectedSession);
        Assert.False(rig.Flow.SelectSession("nope", byUser: false));
        Assert.Equal("sway", rig.Flow.SelectedSession);
        Assert.False(rig.Flow.SelectSession("nope", byUser: true));
        Assert.True(rig.Flow.SelectSession("sway", byUser: true));
        rig.Flow.Choose("grace");

        Assert.Equal("sway", rig.Flow.SelectedSession);
    }

    [Fact]
    public void A_layout_that_does_not_compile_leaves_the_keymap_alone()
    {
        using var rig = new Rig(_root, Settings());
        rig.Flow.Start();
        Assert.True(rig.Flow.SelectLayout("de", byUser: true));

        Assert.False(rig.Flow.SelectLayout("zz-nope", byUser: true));

        Assert.Equal("de", rig.Flow.SelectedLayout);
        Assert.Contains(rig.Steps, step => step.Kind == GreeterStepKind.LayoutFailed && step.Text == "zz-nope");
        Assert.Equal(["XKB_DEFAULT_LAYOUT=de"], rig.Flow.LayoutEnvironment());
    }

    [Fact]
    public void A_free_command_runs_when_allowed_and_is_not_remembered_as_a_session()
    {
        var settings = GreeterSettings.Read(new TomlReader(Toml.ToModel($"""
            [sessions]
            environments = "{Path.Combine(_root, "environments")}"
            allow_command = true
            """), BasinLog.For("test")));
        using var rig = new Rig(_root, settings);
        rig.Greetd.Respond(new GreetdAuthMessage(GreetdAuthMessageType.Secret, "Password:"), new GreetdSuccess(), new GreetdSuccess());
        rig.Flow.Start();
        rig.Flow.ShowUsers();
        rig.Flow.Command = "foot --server";
        rig.Flow.Choose("ada");
        rig.Flow.Answer("pw");
        rig.PumpUntilStep(GreeterStepKind.Started);

        var start = Assert.IsType<GreetdStartSession>(rig.Greetd.Requests()[^1]);
        Assert.Contains("foot --server", string.Join(' ', start.Cmd));
        Assert.Null(GreeterMemory.Load(Path.Combine(_root, "state.toml"), BasinLog.For("test")).SessionOf("ada"));
    }

    [Fact]
    public void Greetd_going_away_before_a_session_starts_closes_the_flow()
    {
        using var rig = new Rig(_root, Settings());
        rig.Greetd.Respond(new GreetdAuthMessage(GreetdAuthMessageType.Secret, "Password:"));
        rig.Flow.Start();
        rig.Flow.ShowUsers();
        rig.Flow.Choose("ada");
        rig.Flow.Answer("pw");
        rig.PumpUntil(() => rig.Flow.LoginState == GreetdLoginState.Waiting || rig.Greetd.Requests().Count >= 2);

        rig.Greetd.Drop();
        rig.PumpUntil(() => rig.ClosedReason is not null);

        Assert.NotNull(rig.ClosedReason);
    }

    [Fact]
    public void Without_a_user_list_the_flow_asks_for_a_name()
    {
        using var rig = new Rig(_root, Settings("\n[users]\nlist = false\n"));
        rig.Flow.Start();
        rig.Flow.ShowUsers();

        Assert.Equal(GreeterPhase.AskingName, rig.Flow.Phase);
        Assert.False(rig.Flow.Choose("ada"));
    }
}
