using Basin.Config;
using Basin.Diagnostics;
using Basin.Greetd;
using Basin.Greetd.Ipc;
using Basin.Ipc;
using Tomlyn;
using Xunit;

namespace Basin.Tests;

public sealed class GreeterIpcTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "basin-greeter-ipc-" + Guid.NewGuid().ToString("N"));

    public GreeterIpcTests()
    {
        Write("etc/passwd", "ada:x:1000:1000:Ada Lovelace:/home/ada:/bin/bash\n");
        Write("etc/login.defs", "UID_MIN 1000\n");
        Write("usr/share/wayland-sessions/sway.desktop", "[Desktop Entry]\nType=Application\nName=Sway\nExec=sway\n");
        Write("environments", "");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void The_flow_methods_answer_over_the_socket()
    {
        using var rig = new IpcTestRig();
        var (loopback, socket) = LoopbackGreetd.Create(rig.Host.Loop);
        using var server = loopback;
        using var client = GreetdClient.Adopt(rig.Host.Loop, socket);
        var settings = GreeterSettings.Read(new TomlReader(Toml.ToModel($"""
            [sessions]
            environments = "{Path.Combine(_root, "environments")}"
            [keyboard]
            layouts = ["us", "de"]
            """), BasinLog.For("test")));
        var memory = GreeterMemory.Load(Path.Combine(_root, "state.toml"), BasinLog.For("test"));
        using var flow = (new GreeterFlow(rig.Host.Loop, client, settings, memory, rig.Host.Seat.Keyboard, new GreeterFlowOptions { Root = _root, Homed = false }));
        GreeterIpc.Register(rig.Server.Methods, new IpcLineReport(), flow);
        flow.Start();
        flow.ShowUsers();
        var peer = rig.Connect();

        Assert.Contains("ERR no prompt is open", peer.Call("""{"method":"greeter/answer","params":{"text":"x"}}"""));

        Assert.Contains("USER ada uid=1000", peer.Call("""{"method":"greeter/users"}"""));
        Assert.Contains("SESSIONS 1", peer.Call("""{"method":"greeter/sessions"}"""));
        Assert.Contains("ERR no listed user nobody", peer.Call("""{"method":"greeter/select","params":{"user":"nobody"}}"""));
        Assert.Contains("ERR no session nope", peer.Call("""{"method":"greeter/session","params":{"id":"nope"}}"""));
        Assert.Contains("ERR no layout fr", peer.Call("""{"method":"greeter/layout","params":{"id":"fr"}}"""));
        peer.Call("""{"method":"greeter/layout","params":{"id":"de"}}""");
        Assert.Equal("de", flow.SelectedLayout);
        peer.Call("""{"method":"greeter/command","params":{"text":"foot"}}""");
        Assert.Equal("foot", flow.Command);

        peer.Call("""{"method":"greeter/select","params":{"user":"ada"}}""");
        PortalBusTests.PumpUntil(rig.Host, () => flow.PromptText is not null && flow.PromptSecret);
        Assert.Contains("STATE phase=conversation user=ada", peer.Call("""{"method":"greeter/state"}"""));
        peer.Call("""{"method":"greeter/cancel"}""");
        PortalBusTests.PumpUntil(rig.Host, () => flow.LoginState == GreetdLoginState.Idle);
        Assert.Equal(GreeterPhase.Users, flow.Phase);

        peer.Call("""{"method":"greeter/other","params":{"user":"grace"}}""");
        PortalBusTests.PumpUntil(rig.Host, () => flow.PromptSecret);
        Assert.Equal("grace", flow.CurrentUser);
        peer.Call("""{"method":"greeter/answer","params":{"text":"demo"}}""");
        PortalBusTests.PumpUntil(rig.Host, () => flow.Phase == GreeterPhase.Started);

        Assert.Equal("grace", GreeterMemory.Load(Path.Combine(_root, "state.toml"), BasinLog.For("test")).LastUser);
    }
}
