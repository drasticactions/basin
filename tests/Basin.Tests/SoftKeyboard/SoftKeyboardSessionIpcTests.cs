using Basin.Ipc;
using Basin.SoftKeyboard;
using Basin.SoftKeyboard.Ipc;
using Xunit;

namespace Basin.Tests;

public sealed class SoftKeyboardSessionIpcTests
{
    [Fact]
    public void The_keyboard_methods_answer_under_the_consumer_namespace()
    {
        using var rig = new IpcTestRig();
        using var session = new SoftKeyboardSession(rig.Host.Loop, new SoftKeyboardSessionOptions { Layouts = ["us-qwerty", "win8-standard-en-us"] });
        session.Attach(rig.Host.Seat.Keyboard, null, null);
        var shown = false;
        var calls = new List<string>();
        var typed = new List<TypedKey>();
        session.Engine.Typed += typed.Add;
        var sizes = new List<KeyboardSize>();
        session.SizeChanged += sizes.Add;
        SoftKeyboardSessionIpc.Register(rig.Server.Methods, new IpcLineReport(), session, new SoftKeyboardIpcHooks
        {
            Namespace = "testwm",
            IsShown = () => shown,
            Show = () =>
            {
                shown = true;
                calls.Add("show");
            },
            Hide = why =>
            {
                shown = false;
                calls.Add("hide " + why);
            },
            Toggle = () => calls.Add("toggle"),
            Report = () => calls.Add("report"),
        });
        session.Engine.Layout(1024, 335, 5);
        var peer = rig.Connect();

        Assert.Contains("not shown", peer.Call("""{"method":"testwm/keyboard-type","params":{"text":"hi"}}"""));
        peer.Call("""{"method":"testwm/keyboard","params":{"state":"show"}}""");
        peer.Call("""{"method":"testwm/keyboard"}""");
        Assert.Equal(["show", "report"], calls);

        peer.Call("""{"method":"testwm/keyboard-type","params":{"text":"Hi!"}}""");
        Assert.Equal("Hi!", string.Concat(typed.Select(static t => t.Text)));
        Assert.Contains("no key", peer.Call("""{"method":"testwm/keyboard-tap","params":{"key":"nope"}}"""));
        Assert.Contains("LONGPRESS", peer.Call("""{"method":"testwm/keyboard-hold","params":{"key":"e","ms":800}}"""));
        peer.Call("""{"method":"testwm/keyboard-choose","params":{"index":1}}""");
        Assert.Equal("è", typed[^1].Text);

        peer.Call("""{"method":"testwm/keyboard-size","params":{"size":"l"}}""");
        Assert.Equal([KeyboardSize.Large], sizes);
        Assert.Contains("no layout fr-azerty", peer.Call("""{"method":"testwm/keyboard-layout","params":{"id":"fr-azerty"}}"""));
        peer.Call("""{"method":"testwm/keyboard-layout","params":{"id":"win8-standard-en-us"}}""");
        Assert.Equal("win8-standard-en-us", session.CurrentLayout.Id);
        Assert.DoesNotContain("error", peer.Call("""{"method":"testwm/keyboard-type","params":{"text":"\ud83d\ude00"}}"""));
        Assert.Equal("\U0001F600", typed[^1].Text);
        peer.Call("""{"method":"testwm/keyboard","params":{"state":"hide"}}""");
        Assert.Equal("hide command", calls[^1]);
    }
}
