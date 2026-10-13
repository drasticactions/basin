using System.Net.Sockets;
using System.Text;
using Basin.Greetd;
using Xunit;

namespace Basin.Tests;

public sealed class GreetdLoginTests
{
    private static void Pump(CompositorTestHost host, Func<bool> condition) => PortalBusTests.PumpUntil(host, condition);

    private static void Settle(CompositorTestHost host, GreetdLogin login) => Pump(host, () => login.State != GreetdLoginState.Waiting && login.State != GreetdLoginState.Starting);

    [Fact]
    public void One_secret_prompt_then_ready_then_started()
    {
        using var host = new CompositorTestHost();
        using var fake = new FakeGreetd();
        fake.Respond(new GreetdAuthMessage(GreetdAuthMessageType.Secret, "Password:"), new GreetdSuccess(), new GreetdSuccess());
        using var client = GreetdClient.Connect(host.Loop, fake.SocketPath);
        using var login = new GreetdLogin(client);
        login.Start("alice");
        Settle(host, login);
        Assert.Equal(GreetdLoginState.Prompting, login.State);
        Assert.Equal(GreetdAuthMessageType.Secret, login.PromptType);
        Assert.Equal("Password:", login.PromptText);
        login.Answer("hunter2"u8);
        Settle(host, login);
        Assert.Equal(GreetdLoginState.Ready, login.State);
        login.StartSession(new GreetdSessionCommand(["sway"], ["XDG_SESSION_TYPE=wayland"]));
        Settle(host, login);
        Assert.Equal(GreetdLoginState.Started, login.State);
        var requests = fake.Requests();
        Assert.Equal(new GreetdCreateSession("alice"), requests[0]);
        Assert.Equal(new GreetdPostAuthMessageResponse("hunter2"), requests[1]);
        var start = Assert.IsType<GreetdStartSession>(requests[2]);
        Assert.Equal(["sway"], start.Cmd);
        Assert.Equal(["XDG_SESSION_TYPE=wayland"], start.Env);
    }

    [Fact]
    public void Visible_then_secret_and_info_then_secret_walk_in_order()
    {
        using var host = new CompositorTestHost();
        using var fake = new FakeGreetd();
        fake.Respond(
            new GreetdAuthMessage(GreetdAuthMessageType.Info, "Touch the key"),
            new GreetdAuthMessage(GreetdAuthMessageType.Visible, "Code:"),
            new GreetdAuthMessage(GreetdAuthMessageType.Error, "Code expired soon"),
            new GreetdAuthMessage(GreetdAuthMessageType.Secret, "Password:"),
            new GreetdSuccess());
        using var client = GreetdClient.Connect(host.Loop, fake.SocketPath);
        using var login = new GreetdLogin(client);
        login.Start("bob");
        Settle(host, login);
        Assert.Equal(GreetdAuthMessageType.Info, login.PromptType);
        Assert.Throws<InvalidOperationException>(() => login.Answer("x"u8));
        login.Acknowledge();
        Settle(host, login);
        Assert.Equal(GreetdAuthMessageType.Visible, login.PromptType);
        login.Answer("123456"u8);
        Settle(host, login);
        Assert.Equal(GreetdAuthMessageType.Error, login.PromptType);
        login.Acknowledge();
        Settle(host, login);
        Assert.Equal(GreetdAuthMessageType.Secret, login.PromptType);
        login.Answer("pw"u8);
        Settle(host, login);
        Assert.Equal(GreetdLoginState.Ready, login.State);
        var requests = fake.Requests();
        Assert.Equal(new GreetdPostAuthMessageResponse(null), requests[1]);
        Assert.Equal(new GreetdPostAuthMessageResponse("123456"), requests[2]);
        Assert.Equal(new GreetdPostAuthMessageResponse(null), requests[3]);
        Assert.Equal("""{"type":"post_auth_message_response"}""", Encoding.UTF8.GetString(fake.Payloads()[1]));
    }

    [Fact]
    public void An_auth_error_sends_cancel_and_leaves_a_login_that_can_start_again()
    {
        using var host = new CompositorTestHost();
        using var fake = new FakeGreetd();
        fake.Respond(new GreetdAuthMessage(GreetdAuthMessageType.Secret, "Password:"), new GreetdError(GreetdErrorType.AuthError, "Login incorrect"), new GreetdSuccess());
        using var client = GreetdClient.Connect(host.Loop, fake.SocketPath);
        using var login = new GreetdLogin(client);
        login.Start("alice");
        Settle(host, login);
        login.Answer("wrong"u8);
        Settle(host, login);
        Assert.Equal(GreetdLoginState.Failed, login.State);
        Assert.Equal(GreetdErrorType.AuthError, login.FailureKind);
        Assert.Equal("Login incorrect", login.FailureDescription);
        Assert.IsType<GreetdCancelSession>(fake.Requests()[2]);
        Assert.True(login.CanStart);
        fake.Respond(new GreetdAuthMessage(GreetdAuthMessageType.Secret, "Password:"));
        login.Start("alice");
        Settle(host, login);
        Assert.Equal(GreetdLoginState.Prompting, login.State);
    }

    [Fact]
    public void An_error_mid_conversation_cancels_and_reports_the_description()
    {
        using var host = new CompositorTestHost();
        using var fake = new FakeGreetd();
        fake.Respond(new GreetdError(GreetdErrorType.Error, "pam_acct_mgmt: account expired"), new GreetdSuccess());
        using var client = GreetdClient.Connect(host.Loop, fake.SocketPath);
        using var login = new GreetdLogin(client);
        login.Start("carol");
        Settle(host, login);
        Assert.Equal(GreetdLoginState.Failed, login.State);
        Assert.Equal(GreetdErrorType.Error, login.FailureKind);
        Assert.Equal("pam_acct_mgmt: account expired", login.FailureDescription);
        Assert.IsType<GreetdCancelSession>(fake.Requests()[^1]);
    }

    [Fact]
    public void A_second_start_while_one_is_open_throws_and_cancel_returns_to_idle()
    {
        using var host = new CompositorTestHost();
        using var fake = new FakeGreetd();
        fake.Respond(new GreetdAuthMessage(GreetdAuthMessageType.Secret, "Password:"));
        using var client = GreetdClient.Connect(host.Loop, fake.SocketPath);
        using var login = new GreetdLogin(client);
        login.Start("alice");
        Assert.Throws<InvalidOperationException>(() => login.Start("bob"));
        Assert.Throws<InvalidOperationException>(() => client.Send(new GreetdCancelSession()));
        Settle(host, login);
        login.Cancel();
        Settle(host, login);
        Assert.Equal(GreetdLoginState.Idle, login.State);
        Assert.IsType<GreetdCancelSession>(fake.Requests()[^1]);
    }

    [Fact]
    public void A_cancel_while_a_reply_is_outstanding_waits_for_it()
    {
        using var host = new CompositorTestHost();
        using var fake = new FakeGreetd();
        fake.Respond(new GreetdAuthMessage(GreetdAuthMessageType.Secret, "Password:"));
        using var client = GreetdClient.Connect(host.Loop, fake.SocketPath);
        using var login = new GreetdLogin(client);
        login.Start("alice");
        login.Cancel();
        Pump(host, () => login.State == GreetdLoginState.Idle);
        Assert.Equal(2, fake.Requests().Count);
        Assert.IsType<GreetdCancelSession>(fake.Requests()[1]);
    }

    [Fact]
    public void A_closed_socket_fails_the_login_and_raises_closed()
    {
        using var host = new CompositorTestHost();
        using var fake = new FakeGreetd();
        fake.Respond(new GreetdAuthMessage(GreetdAuthMessageType.Secret, "Password:"), null);
        using var client = GreetdClient.Connect(host.Loop, fake.SocketPath);
        using var login = new GreetdLogin(client);
        string? reason = null;
        client.Closed += r => reason = r;
        login.Start("alice");
        Settle(host, login);
        login.Answer("pw"u8);
        Pump(host, () => reason is not null);
        Assert.Equal(GreetdLoginState.Failed, login.State);
        Assert.Equal("greetd closed the connection", reason);
        Assert.False(login.CanStart);
        Assert.Throws<InvalidOperationException>(() => client.Send(new GreetdCancelSession()));
    }

    [Fact]
    public void An_oversized_frame_closes_the_connection()
    {
        using var host = new CompositorTestHost();
        var pair = Pair();
        using var server = pair.Server;
        using var client = GreetdClient.Adopt(host.Loop, pair.Client);
        string? reason = null;
        client.Closed += r => reason = r;
        client.Send(new GreetdCancelSession());
        var header = new byte[4];
        GreetdFrame.WriteHeader(header, GreetdFrame.MaxLength + 1);
        server.Send(header);
        Pump(host, () => reason is not null);
        Assert.Contains("over", reason);
    }

    [Fact]
    public void A_response_split_across_writes_is_reassembled()
    {
        using var host = new CompositorTestHost();
        var pair = Pair();
        using var server = pair.Server;
        using var client = GreetdClient.Adopt(host.Loop, pair.Client);
        GreetdResponse? got = null;
        client.Response += r => got = r;
        client.Send(new GreetdCancelSession());
        using var writer = new GreetdFrameWriter();
        writer.Write(new GreetdAuthMessage(GreetdAuthMessageType.Info, "hello"));
        var frame = writer.Frame.ToArray();
        server.Send(frame.AsSpan(0, 3));
        for (var i = 0; i < 5; i++)
        {
            host.Loop.Dispatch(5);
        }

        Assert.Null(got);
        server.Send(frame.AsSpan(3));
        Pump(host, () => got is not null);
        Assert.Equal("hello", Assert.IsType<GreetdAuthMessage>(got).Message);
    }

    private static (Socket Server, Socket Client) Pair()
    {
        var directory = Path.Combine(Path.GetTempPath(), "basin-greetd-pair-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "s");
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen(1);
        var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        client.Connect(new UnixDomainSocketEndPoint(path));
        var server = listener.Accept();
        Directory.Delete(directory, true);
        return (server, client);
    }
}
