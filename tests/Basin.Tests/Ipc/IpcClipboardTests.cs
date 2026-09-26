using System.Text;
using System.Text.Json;
using Basin.Capabilities;
using Basin.Ipc;
using Xunit;

namespace Basin.Tests;

public sealed class IpcClipboardTests
{
    private static JsonElement Parse(string frame) => JsonDocument.Parse(frame).RootElement.Clone();

    private static string? ErrorCode(JsonElement reply) =>
        reply.TryGetProperty("error", out var error) ? error.GetProperty("code").GetString() : null;

    private static (IpcTestRig Rig, TestSelectionStore Store) Rig()
    {
        var store = new TestSelectionStore();
        var rig = new IpcTestRig(services => services.Use<ISelectionStore>(store));
        _ = rig.Own(store);
        return (rig, store);
    }

    private static string Link(int fd) => new FileInfo($"/proc/self/fd/{fd}").LinkTarget ?? string.Empty;

    private static int Ends(string pipe) =>
        Directory.GetFiles("/proc/self/fd").Count(path => (new FileInfo(path).LinkTarget ?? string.Empty) == pipe);

    private static void Warm(IpcTestPeer peer, TestSelectionStore store)
    {
        store.Offer(SelectionKind.Clipboard, ("text/plain", [65]));
        Assert.Equal("A", Parse(peer.Call("""{"method":"clipboard/read","params":{"timeout_ms":5000}}""")).GetProperty("result").GetProperty("text").GetString());
        store.Received.Clear();
    }

    [Fact]
    public void Reads_text_from_the_clipboard_and_the_primary_selection()
    {
        var (rig, store) = Rig();
        using var owner = rig;
        store.Offer(SelectionKind.Clipboard, ("image/png", [1, 2]), ("text/plain", Encoding.UTF8.GetBytes("plain")), ("text/plain;charset=utf-8", Encoding.UTF8.GetBytes("héllo")));
        store.Offer(SelectionKind.Primary, ("UTF8_STRING", Encoding.UTF8.GetBytes("primary text")));
        var peer = rig.Connect();

        var clipboard = Parse(peer.Call("""{"method":"clipboard/read"}""")).GetProperty("result");
        Assert.Equal("héllo", clipboard.GetProperty("text").GetString());
        Assert.Equal("text/plain;charset=utf-8", clipboard.GetProperty("mime").GetString());
        Assert.Equal(["image/png", "text/plain", "text/plain;charset=utf-8"], clipboard.GetProperty("types").EnumerateArray().Select(t => t.GetString()));

        var primary = Parse(peer.Call("""{"method":"clipboard/read","params":{"kind":"primary"}}""")).GetProperty("result");
        Assert.Equal("primary text", primary.GetProperty("text").GetString());

        var asked = Parse(peer.Call("""{"method":"clipboard/read","params":{"mime":"text/plain"}}""")).GetProperty("result");
        Assert.Equal("plain", asked.GetProperty("text").GetString());
        Assert.Equal(["text/plain;charset=utf-8", "UTF8_STRING", "text/plain"], store.Received);
    }

    [Fact]
    public void Empty_and_non_text_selections_answer_at_once()
    {
        var (rig, store) = Rig();
        using var owner = rig;
        var peer = rig.Connect();
        var empty = Parse(peer.Call("""{"method":"clipboard/read"}""")).GetProperty("result");
        Assert.Equal(0, empty.GetProperty("types").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("mime").ValueKind);
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("text").ValueKind);

        store.Offer(SelectionKind.Clipboard, ("image/png", [1]));
        var image = Parse(peer.Call("""{"method":"clipboard/read"}""")).GetProperty("result");
        Assert.Equal("image/png", image.GetProperty("types")[0].GetString());
        Assert.Equal(JsonValueKind.Null, image.GetProperty("text").ValueKind);
        Assert.Empty(store.Received);

        Assert.Equal(IpcErrorCodes.InvalidParams, ErrorCode(Parse(peer.Call("""{"method":"clipboard/read","params":{"mime":"image/png"}}"""))));
        Assert.Equal(IpcErrorCodes.InvalidParams, ErrorCode(Parse(peer.Call("""{"method":"clipboard/read","params":{"kind":"secondary"}}"""))));
        Assert.Equal(IpcErrorCodes.InvalidParams, ErrorCode(Parse(peer.Call("""{"method":"clipboard/read","params":{"max_bytes":20000000}}"""))));
        Assert.Equal(IpcErrorCodes.NotFound, ErrorCode(Parse(peer.Call("""{"method":"clipboard/read","params":{"mime":"text/html"}}"""))));
    }

    [Fact]
    public void An_owner_that_never_writes_fails_at_the_timeout_and_closes_the_pipe()
    {
        var (rig, store) = Rig();
        using var owner = rig;
        var peer = rig.Connect();
        Warm(peer, store);
        store.Offer(SelectionKind.Clipboard, ("text/plain", null));
        var reply = Parse(peer.Call("""{"method":"clipboard/read","params":{"timeout_ms":30}}"""));
        Assert.Equal(IpcErrorCodes.Failed, ErrorCode(reply));
        var pipe = Link(Assert.Single(store.Held));
        Assert.StartsWith("pipe:", pipe);
        Assert.Equal(1, Ends(pipe));
        store.Dispose();
        Assert.Equal(0, Ends(pipe));
    }

    [Fact]
    public void A_selection_over_max_bytes_is_too_large()
    {
        var (rig, store) = Rig();
        using var owner = rig;
        var peer = rig.Connect();
        Warm(peer, store);
        store.Offer(SelectionKind.Clipboard, ("text/plain", new byte[200_000]));
        var pipes = Directory.GetFiles("/proc/self/fd").Select(path => new FileInfo(path).LinkTarget ?? string.Empty).Where(link => link.StartsWith("pipe:", StringComparison.Ordinal)).ToHashSet();
        var reply = Parse(peer.Call("""{"method":"clipboard/read","params":{"max_bytes":1000}}"""));
        Assert.Equal(IpcErrorCodes.TooLarge, ErrorCode(reply));
        store.Dispose();
        var after = Directory.GetFiles("/proc/self/fd").Select(path => new FileInfo(path).LinkTarget ?? string.Empty).Where(link => link.StartsWith("pipe:", StringComparison.Ordinal)).ToHashSet();
        Assert.Subset(pipes, after);
    }

    [Fact]
    public void A_connection_that_closes_while_reading_closes_the_read_end()
    {
        var (rig, store) = Rig();
        using var owner = rig;
        var peer = rig.Connect();
        Warm(peer, store);
        store.Offer(SelectionKind.Clipboard, ("text/plain", null));
        peer.Send("""{"method":"clipboard/read","params":{"timeout_ms":60000}}""");
        for (var i = 0; i < 20 && store.Received.Count == 0; i++)
        {
            rig.Pump();
        }

        Assert.Single(store.Received);
        var pipe = Link(Assert.Single(store.Held));
        Assert.Equal(CompositorTestHost.TransportUnderTest == Basin.Cli.TransportKind.Managed ? 2 : 3, Ends(pipe));
        peer.Dispose();
        for (var i = 0; i < 50 && rig.Server.ConnectionCount > 0; i++)
        {
            rig.Pump();
        }

        Assert.Equal(0, rig.Server.ConnectionCount);
        Assert.Equal(1, Ends(pipe));
        store.Dispose();
        Assert.Equal(0, Ends(pipe));
    }

    [Fact]
    public void Clipboard_changed_attaches_with_its_first_subscriber()
    {
        var (rig, store) = Rig();
        using var owner = rig;
        var peer = rig.Connect();
        Assert.Equal(0, store.Handlers);
        _ = peer.Call("""{"method":"ipc/subscribe","params":{"events":["clipboard/changed"]}}""");
        Assert.Equal(1, store.Handlers);

        store.Offer(SelectionKind.Clipboard, ("text/plain", [65]));
        store.Offer(SelectionKind.Clipboard, ("text/plain", [66]));
        store.Offer(SelectionKind.Primary, ("text/plain", [67]));
        var first = Parse(peer.Receive());
        var second = Parse(peer.Receive());
        Assert.Equal("clipboard/changed", first.GetProperty("event").GetString());
        Assert.Equal("clipboard", first.GetProperty("data").GetProperty("kind").GetString());
        Assert.Equal("primary", second.GetProperty("data").GetProperty("kind").GetString());
        Assert.False(peer.HasFrame());

        _ = peer.Call("""{"method":"ipc/unsubscribe","params":{"events":["clipboard/changed"]}}""");
        Assert.Equal(0, store.Handlers);
    }
}
