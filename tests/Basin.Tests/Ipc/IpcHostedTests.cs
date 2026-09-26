using System.Text.Json;
using Basin.Diagnostics;
using Basin.Ipc;
using Basin.Shell.Nested;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class IpcHostedTests
{
    private static JsonElement Parse(string frame) => JsonDocument.Parse(frame).RootElement.Clone();

    private static string? ErrorCode(string frame) =>
        Parse(frame).TryGetProperty("error", out var error) ? error.GetProperty("code").GetString() : null;

    private static ulong IdOf(IpcTestPeer peer, string appId)
    {
        var list = Parse(peer.Call("""{"method":"windows/list"}""")).GetProperty("result").GetProperty("windows");
        foreach (var window in list.EnumerateArray())
        {
            if (window.GetProperty("app_id").GetString() == appId)
            {
                return window.GetProperty("id").GetUInt64();
            }
        }

        throw new Xunit.Sdk.XunitException($"no window {appId} in windows/list");
    }

    private static uint CenterPixel(JsonElement capture)
    {
        var (rgba, width, height) = PngCodec.Decode(capture.GetProperty("png").GetBytesFromBase64());
        var at = (((height / 2) * width) + (width / 2)) * 4;
        return (uint)((rgba[at + 3] << 24) | (rgba[at] << 16) | (rgba[at + 1] << 8) | rgba[at + 2]);
    }

    [Fact]
    public void A_hosted_shell_lists_windows_and_captures_one_that_is_covered()
    {
        using var rig = new IpcHostedRig();
        var peer = rig.Connect();
        rig.Harness.MapToplevel(200, 150, appId: "org.basin.under", color: 0xFFCC2020);
        rig.Harness.MapToplevel(200, 150, appId: "org.basin.over", color: 0xFF20CC20);
        var under = IdOf(peer, "org.basin.under");
        var over = IdOf(peer, "org.basin.over");

        var methods = peer.Call("""{"method":"ipc/methods"}""");
        foreach (var name in new[] { "capture/window", "windows/stack", "input/pointer-button", "windows/move" })
        {
            Assert.Contains($"\"{name}\"", methods);
        }

        var target = Parse(peer.Call($$$"""{"method":"windows/get","params":{"id":{{{under}}}}}""")).GetProperty("result").GetProperty("geometry");
        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"windows/move","params":{"id":{{{over}}},"x":{{{target.GetProperty("x").GetInt32()}}},"y":{{{target.GetProperty("y").GetInt32()}}}}}""")));
        rig.Harness.Pump();

        var stack = Parse(peer.Call("""{"method":"windows/stack"}""")).GetProperty("result").GetProperty("ids")
            .EnumerateArray().Select(e => e.GetUInt64()).ToArray();
        Assert.Equal([under, over], stack);

        var capture = Parse(peer.Call($$$"""{"method":"capture/window","params":{"id":{{{under}}},"client_only":true,"to":"inline"}}"""));
        Assert.Equal(0xFFCC2020u, CenterPixel(capture.GetProperty("result")));
        var region = rig.Harness.Shell.Windows.Single(w => w.Content.AppId == "org.basin.under").ClientBox;
        var shown = Parse(peer.Call($$$"""{"method":"capture/region","params":{"x":{{{region.X}}},"y":{{{region.Y}}},"width":{{{region.Width}}},"height":{{{region.Height}}},"to":"inline"}}"""));
        Assert.Equal(0xFF20CC20u, CenterPixel(shown.GetProperty("result")));
    }

    [Fact]
    public void The_shell_answers_state_requests_and_refuses_to_move_a_maximized_window()
    {
        using var rig = new IpcHostedRig();
        var peer = rig.Connect();
        rig.Harness.MapToplevel(200, 150, appId: "org.basin.one");
        var id = IdOf(peer, "org.basin.one");
        var window = Assert.Single(rig.Harness.Shell.Windows);

        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"windows/set-state","params":{"id":{{{id}}},"maximized":true}}""")));
        Assert.True(window.Maximized);
        Assert.Equal((rig.Harness.Shell.WorkArea.X, rig.Harness.Shell.WorkArea.Y), (window.FrameBox.X, window.FrameBox.Y));

        var refused = Parse(peer.Call($$$"""{"method":"windows/move","params":{"id":{{{id}}},"x":5,"y":5}}"""));
        Assert.Equal(IpcErrorCodes.Refused, refused.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("maximized", refused.GetProperty("error").GetProperty("message").GetString());

        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"windows/set-state","params":{"id":{{{id}}},"maximized":false}}""")));
        Assert.False(window.Maximized);
        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"windows/resize","params":{"id":{{{id}}},"width":300,"height":200}}""")));
        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"windows/set-state","params":{"id":{{{id}}},"minimized":true}}""")));
        Assert.True(window.Minimized);
        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"windows/activate","params":{"id":{{{id}}}}}""")));
        Assert.False(window.Minimized);
        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"windows/close","params":{"id":{{{id}}}}}""")));
    }

    [Fact]
    public void Input_goes_through_the_shell_and_reaches_the_client_at_surface_coordinates()
    {
        using var rig = new IpcHostedRig();
        var peer = rig.Connect();
        rig.Harness.MapToplevel(200, 150, appId: "org.basin.click");
        var pointer = rig.Harness.Client.Seat!.GetPointer();
        var log = new List<string>();
        pointer.Enter += (_, e) => log.Add($"enter {e.SurfaceX.ToDouble():F0},{e.SurfaceY.ToDouble():F0}");
        pointer.Button += (_, e) => log.Add(e.State == WlPointer.ButtonState.Pressed ? "press" : "release");
        rig.Harness.Pump();
        var client = Assert.Single(rig.Harness.Shell.Windows).ClientBox;

        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"input/pointer-move","params":{"x":{{{client.X + 30}}},"y":{{{client.Y + 40}}}}}""")));
        Assert.Null(ErrorCode(peer.Call("""{"method":"input/pointer-button","params":{"button":"left"}}""")));
        rig.Harness.PumpUntil(() => log.Contains("release"), "the click never reached the client");
        Assert.Equal(["enter 30,40", "press", "release"], log);
        pointer.Dispose();
    }

    [Fact]
    public void A_window_relative_click_is_refused_on_a_covered_point_and_reaches_the_client_when_raised()
    {
        using var rig = new IpcHostedRig();
        var peer = rig.Connect();
        var under = rig.Harness.MapToplevel(200, 150, appId: "org.basin.under");
        rig.Harness.MapToplevel(200, 150, appId: "org.basin.over");
        var underId = IdOf(peer, "org.basin.under");
        var overId = IdOf(peer, "org.basin.over");
        var target = rig.Harness.Shell.Windows.Single(w => w.Content.AppId == "org.basin.under").ClientBox;
        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"windows/move","params":{"id":{{{overId}}},"x":{{{target.X + 100}}},"y":{{{target.Y + 60}}}}}""")));

        var pointer = rig.Harness.Client.Seat!.GetPointer();
        var log = new List<string>();
        pointer.Enter += (_, e) => log.Add($"{(e.Surface == under.Surface ? "under" : "other")} {e.SurfaceX.ToDouble():F0},{e.SurfaceY.ToDouble():F0}");
        pointer.Motion += (_, e) => log.Add($"motion {e.SurfaceX.ToDouble():F0},{e.SurfaceY.ToDouble():F0}");
        pointer.Button += (_, e) => log.Add(e.State == WlPointer.ButtonState.Pressed ? "press" : "release");
        rig.Harness.Pump();

        var covered = Parse(peer.Call($$$"""{"method":"input/pointer-button","params":{"button":"left","window":"{{{underId}}}","x":150,"y":100}}"""));
        Assert.Equal(IpcErrorCodes.Refused, covered.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal($"window {underId} is covered at (150, 100) by window {overId}", covered.GetProperty("error").GetProperty("message").GetString());
        rig.Harness.Pump();
        Assert.DoesNotContain("press", log);

        Assert.Equal(IpcErrorCodes.InvalidParams, ErrorCode(peer.Call($$$"""{"method":"input/pointer-move","params":{"window":{{{underId}}},"x":250,"y":10}}""")));
        Assert.Equal(IpcErrorCodes.InvalidParams, ErrorCode(peer.Call($$$"""{"method":"input/pointer-button","params":{"button":"left","window":{{{underId}}}}}""")));
        Assert.Equal(IpcErrorCodes.NotFound, ErrorCode(peer.Call("""{"method":"input/pointer-move","params":{"window":424242,"x":1,"y":1}}""")));

        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"input/pointer-button","params":{"button":"left","window":{{{underId}}},"x":150,"y":100,"raise":true}}""")));
        rig.Harness.PumpUntil(() => log.Contains("release"), "the raised click never reached the client");
        Assert.Contains("under 150,100", log);
        Assert.Equal(["press", "release"], log.Where(l => l is "press" or "release"));

        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"windows/set-state","params":{"id":{{{underId}}},"minimized":true}}""")));
        Assert.Equal(IpcErrorCodes.Refused, ErrorCode(peer.Call($$$"""{"method":"input/pointer-move","params":{"window":{{{underId}}},"x":5,"y":5}}""")));
        pointer.Dispose();
    }

    [Fact]
    public void Wait_idle_completes_once_a_window_stops_drawing()
    {
        using var rig = new IpcHostedRig();
        var peer = rig.Connect();
        var window = rig.Harness.MapToplevel(200, 150, appId: "org.basin.busy");
        var id = IdOf(peer, "org.basin.busy");

        peer.Send($$$"""{"id":1,"method":"windows/wait-idle","params":{"id":{{{id}}},"quiet_ms":80,"timeout_ms":3000}}""");
        for (var i = 0; i < 6; i++)
        {
            rig.Harness.Commit(window, 200, 150);
            rig.Harness.Pump();
            Assert.False(peer.HasFrame(), "wait-idle answered while the window was drawing");
            Thread.Sleep(20);
        }

        var frame = peer.Receive(400);
        var reply = Parse(frame).GetProperty("result");
        Assert.True(reply.GetProperty("commits").GetInt64() >= 5, frame);
        Assert.True(reply.GetProperty("waited_ms").GetInt64() >= 80);
    }

    [Fact]
    public void Wait_idle_ignores_small_damage_and_times_out_on_large_damage()
    {
        using var rig = new IpcHostedRig();
        var peer = rig.Connect();
        var window = rig.Harness.MapToplevel(200, 150, appId: "org.basin.cursor");
        var id = IdOf(peer, "org.basin.cursor");

        peer.Send($$$"""{"id":1,"method":"windows/wait-idle","params":{"id":{{{id}}},"quiet_ms":60,"timeout_ms":3000}}""");
        var answered = false;
        for (var i = 0; i < 40 && !answered; i++)
        {
            window.Surface.Attach(window.Buffer!.Proxy, 0, 0);
            window.Surface.Damage(10, 10, 8, 16);
            window.Surface.Commit();
            rig.Harness.Pump();
            answered = peer.HasFrame();
            Thread.Sleep(15);
        }

        var small = Parse(peer.Receive()).GetProperty("result");
        Assert.True(small.GetProperty("ignored").GetInt64() > 0);
        Assert.Equal(0, small.GetProperty("commits").GetInt64());

        peer.Send($$$"""{"id":2,"method":"windows/wait-idle","params":{"id":{{{id}}},"quiet_ms":200,"timeout_ms":120}}""");
        answered = false;
        for (var i = 0; i < 40 && !answered; i++)
        {
            rig.Harness.Commit(window, 200, 150);
            rig.Harness.Pump();
            answered = peer.HasFrame();
            Thread.Sleep(15);
        }

        var timedOut = Parse(peer.Receive()).GetProperty("error");
        Assert.Equal(IpcErrorCodes.Failed, timedOut.GetProperty("code").GetString());
        Assert.Contains("the last commit was", timedOut.GetProperty("message").GetString());
        Assert.Equal(IpcErrorCodes.NotFound, ErrorCode(peer.Call("""{"method":"windows/wait-idle","params":{"id":424242}}""")));
    }

    [Fact]
    public void Text_goes_to_an_enabled_text_input_whole_and_falls_back_to_the_keymap()
    {
        using var rig = new IpcHostedRig();
        var peer = rig.Connect();
        rig.Harness.MapToplevel(200, 150, appId: "org.basin.field");
        var client = rig.Harness.Client;
        var manager = client.Registry.Bind<Basin.Desktop.Protocol.ZwpTextInputManagerV3>(
            client.Globals.First(g => g.Interface == "zwp_text_input_manager_v3").Name, 1);
        var textInput = manager.GetTextInput(client.Seat!);
        var entered = false;
        var commits = new List<string>();
        textInput.Enter += (_, _) => entered = true;
        textInput.CommitString += (_, e) => commits.Add(e.Text ?? string.Empty);
        rig.Harness.PumpUntil(() => entered, "the text input never entered the focused surface");

        Assert.Equal(IpcErrorCodes.Refused, ErrorCode(peer.Call("""{"method":"input/text","params":{"text":"x","via":"text-input"}}""")));
        var keymap = Parse(peer.Call("""{"method":"input/text","params":{"text":"aé"}}"""));
        Assert.Equal(IpcErrorCodes.InvalidParams, keymap.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("position 1", keymap.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal("keymap", Parse(peer.Call("""{"method":"input/text","params":{"text":"ab"}}""")).GetProperty("result").GetProperty("via").GetString());

        textInput.Enable();
        textInput.Commit();
        rig.Harness.Pump();
        var reply = Parse(peer.Call("""{"method":"input/text","params":{"text":"naïve ✓ 日本"}}"""));
        Assert.Equal("text-input", reply.GetProperty("result").GetProperty("via").GetString());
        rig.Harness.PumpUntil(() => commits.Count > 0, "the commit never reached the client");
        Assert.Equal(["naïve ✓ 日本"], commits);

        Assert.Equal("keymap", Parse(peer.Call("""{"method":"input/text","params":{"text":"ab","via":"keymap"}}""")).GetProperty("result").GetProperty("via").GetString());
        Assert.Single(commits);
        textInput.Dispose();
        manager.Dispose();
    }

    [Fact]
    public void Clipboard_write_is_read_back_through_the_seat()
    {
        using var rig = new IpcHostedRig();
        var peer = rig.Connect();
        Assert.Null(ErrorCode(peer.Call("""{"method":"clipboard/write","params":{"text":"hello, ✓"}}""")));
        peer.Send("""{"id":2,"method":"clipboard/read"}""");
        var read = Parse(peer.Receive()).GetProperty("result");
        Assert.Equal("hello, ✓", read.GetProperty("text").GetString());
        Assert.Contains("text/plain;charset=utf-8", read.GetProperty("types").EnumerateArray().Select(t => t.GetString()));

        var large = new string('x', 300_000);
        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"clipboard/write","params":{"text":"{{{large}}}","kind":"primary"}}""")));
        peer.Send("""{"id":3,"method":"clipboard/read","params":{"kind":"primary"}}""");
        Assert.Equal(large, Parse(peer.Receive(2000)).GetProperty("result").GetProperty("text").GetString());
        Assert.Equal(IpcErrorCodes.InvalidParams, ErrorCode(peer.Call("""{"method":"clipboard/write","params":{"text":"x","kind":"secondary"}}""")));
    }

    [Fact]
    public void An_excluded_node_is_missing_from_every_capture_and_refuses_synthetic_input()
    {
        using var rig = new IpcHostedRig();
        var peer = rig.Connect();
        rig.Harness.MapToplevel(200, 150, appId: "org.basin.behind", color: 0xFFCC2020);
        var id = IdOf(peer, "org.basin.behind");
        var window = Assert.Single(rig.Harness.Shell.Windows);
        var client = window.ClientBox;
        var dialog = new Basin.Scene.SceneRect(rig.Harness.Shell.Layers.Switcher, 60, 40, new RenderColor(0.1f, 0.1f, 0.9f, 1f))
        {
            ExcludedFromCapture = true,
        };
        dialog.SetPosition(client.X + 70, client.Y + 55);
        var inner = new Basin.Scene.SceneRect(window.Tree!, 20, 20, new RenderColor(0.1f, 0.9f, 0.1f, 1f)) { ExcludedFromCapture = true };
        inner.SetPosition(window.ClientBox.X - window.X + 90, window.ClientBox.Y - window.Y + 65);

        var region = Parse(peer.Call($$$"""{"method":"capture/region","params":{"x":{{{client.X}}},"y":{{{client.Y}}},"width":{{{client.Width}}},"height":{{{client.Height}}},"to":"inline"}}"""));
        Assert.Equal(0xFFCC2020u, CenterPixel(region.GetProperty("result")));
        var output = Parse(peer.Call("""{"method":"capture/output","params":{"to":"inline"}}"""));
        var (rgba, width, _) = PngCodec.Decode(output.GetProperty("result").GetProperty("png").GetBytesFromBase64());
        var at = (((client.Y + 75) * width) + client.X + 100) * 4;
        Assert.Equal((0xCC, 0x20, 0x20), (rgba[at], rgba[at + 1], rgba[at + 2]));
        var captured = Parse(peer.Call($$$"""{"method":"capture/window","params":{"id":{{{id}}},"client_only":true,"to":"inline"}}"""));
        Assert.Equal(0xFFCC2020u, CenterPixel(captured.GetProperty("result")));

        var refused = Parse(peer.Call($$$"""{"method":"input/pointer-move","params":{"x":{{{client.X + 100}}},"y":{{{client.Y + 75}}}}}"""));
        Assert.Equal(IpcErrorCodes.Refused, refused.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("excluded", refused.GetProperty("error").GetProperty("message").GetString());
        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"input/pointer-move","params":{"x":{{{client.X + 5}}},"y":{{{client.Y + 5}}}}}""")));
        Assert.Null(ErrorCode(peer.Call("""{"method":"input/key","params":{"code":30}}""")));

        window.ContentNode!.ExcludedFromCapture = true;
        Assert.True(rig.Harness.Shell.IsKeyboardFocusExcluded());
        Assert.Equal(IpcErrorCodes.Refused, ErrorCode(peer.Call("""{"method":"input/key","params":{"code":30}}""")));
        Assert.Equal(IpcErrorCodes.Refused, ErrorCode(peer.Call("""{"method":"input/text","params":{"text":"a"}}""")));
        Assert.Equal(IpcErrorCodes.Refused, ErrorCode(peer.Call("""{"method":"input/chord","params":{"chord":"Alt+a"}}""")));
        Assert.Equal(IpcErrorCodes.Refused, ErrorCode(peer.Call("""{"method":"input/pointer-button","params":{"button":"left"}}""")));
        window.ContentNode!.ExcludedFromCapture = false;
        inner.Destroy();
        dialog.Destroy();
    }

    [Fact]
    public void Adopted_content_is_published_into_the_toplevel_model()
    {
        using var rig = new IpcHostedRig();
        var peer = rig.Connect();
        var content = new StubContent("adopted", "org.basin.adopted");
        var window = rig.Harness.Shell.Adopt(content);
        var id = IdOf(peer, "org.basin.adopted");
        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"windows/set-state","params":{"id":{{{id}}},"maximized":true}}""")));
        Assert.True(content.Maximized);
        rig.Harness.Shell.Release(window);
        var list = peer.Call("""{"method":"windows/list"}""");
        Assert.DoesNotContain("org.basin.adopted", list);
    }

    [Fact]
    public void Maximize_placement_maps_every_normal_window_maximized()
    {
        using var rig = new IpcHostedRig(new ShellSettings { Placement = PlacementMode.Maximize });
        rig.Harness.MapToplevel(200, 150, appId: "org.basin.first");
        rig.Harness.MapToplevel(200, 150, appId: "org.basin.second");
        Assert.All(rig.Harness.Shell.Windows, window => Assert.True(window.Maximized));
    }

    [Fact]
    public void Maximize_placement_centers_a_compositor_dialog_instead_of_maximizing_it()
    {
        using var rig = new IpcHostedRig(new ShellSettings { Placement = PlacementMode.Maximize });
        var shell = rig.Harness.Shell;
        var window = shell.Adopt(new StubContent("Approve", "org.basin.dialog", centered: true), publish: false);
        Assert.False(window.Maximized);
        var area = shell.WorkArea;
        var frame = window.FrameBox;
        Assert.InRange(frame.X + (frame.Width / 2) - (area.X + (area.Width / 2)), -1, 1);
        Assert.InRange(frame.Y + (frame.Height / 2) - (area.Y + (area.Height / 2)), -1, 1);
        shell.Release(window);
    }

    private sealed class StubContent(string title, string appId, bool centered = false) : IWindowContent
    {
        private Basin.Scene.SceneTree? _tree;

        public string Title => title;

        public string AppId => appId;

        public Box Geometry { get; private set; } = new(0, 0, 120, 80);

        public int MinWidth => 0;

        public int MinHeight => 0;

        public int MaxWidth => 0;

        public int MaxHeight => 0;

        public IWindowContent? Parent => null;

        public Surface? Surface => null;

        public Basin.Capabilities.IUISurface? UISurface => null;

        public bool Maximized { get; private set; }

        public bool Fullscreen { get; private set; }

        public bool Resizing => false;

        public bool ServerDecorated => false;

        public bool Centered => centered;

        public Basin.Capabilities.FrameCapabilities Capabilities => default;

        public event Action? TitleChanged { add { } remove { } }

        public event Action? AppIdChanged { add { } remove { } }

        public event Action? ParentChanged { add { } remove { } }

        public event Action? DecorationsChanged { add { } remove { } }

        public event Action? Committed { add { } remove { } }

        public Basin.Scene.SceneNode Attach(Basin.Scene.SceneTree tree)
        {
            _tree = new Basin.Scene.SceneTree(tree);
            return _tree;
        }

        public void Detach()
        {
            _tree?.Destroy();
            _tree = null;
        }

        public void SetActivated(bool activated)
        {
        }

        public void SetMaximized(bool maximized) => Maximized = maximized;

        public void SetFullscreen(bool fullscreen) => Fullscreen = fullscreen;

        public void SetResizing(bool resizing)
        {
        }

        public void SetMinimized(bool minimized)
        {
        }

        public void Raise()
        {
        }

        public void Lower()
        {
        }

        public void SetTiled(Basin.Shell.Xdg.ResizeEdges edges)
        {
        }

        public void SetSize(int width, int height)
        {
            if (width > 0 && height > 0)
            {
                Geometry = new Box(0, 0, width, height);
            }
        }

        public void SetBounds(int width, int height)
        {
        }

        public void SetPosition(int x, int y)
        {
        }

        public void OutputScaleChanged(double scale)
        {
        }

        public void ReportGeometry(in Box frame, in Box client)
        {
        }

        public void Close()
        {
        }

        public bool Owns(Surface surface) => false;
    }
}
