using System.Numerics;
using System.Text;
using Basin.Capabilities;
using Basin.UI.ImGui;
using Hexa.NET.ImGui;
using Xunit;
using ImGuiApi = Hexa.NET.ImGui.ImGui;

namespace Basin.Tests;

public sealed unsafe class ImGuiSurfaceTests
{
    private const uint BtnLeft = 0x110;
    private const uint KeyH = 35;
    private const uint KeyI = 23;
    private const uint KeyLeftCtrl = 29;
    private const uint KeyEnter = 28;

    internal delegate void Builder(ImGuiFrame frame);

    [Fact]
    public void The_four_native_libraries_carry_the_same_imgui()
    {
        Assert.True(ImGuiNative.TryLoad(out var declined), declined);
        Assert.Equal(ImGuiNative.BindingVersion, ImGuiNative.Version);
    }

    [Fact]
    public void Evdev_keys_map_to_the_imgui_keys_named_for_their_position()
    {
        Assert.Equal(ImGuiKey.A, EvdevImGuiKeys.KeyOf(30));
        Assert.Equal(ImGuiKey.Enter, EvdevImGuiKeys.KeyOf(KeyEnter));
        Assert.Equal(ImGuiKey.Escape, EvdevImGuiKeys.KeyOf(1));
        Assert.Equal(ImGuiKey.LeftArrow, EvdevImGuiKeys.KeyOf(105));
        Assert.Equal(ImGuiKey.LeftCtrl, EvdevImGuiKeys.KeyOf(KeyLeftCtrl));
        Assert.Equal(ImGuiKey.KeypadEnter, EvdevImGuiKeys.KeyOf(96));
        Assert.Equal(ImGuiKey.F12, EvdevImGuiKeys.KeyOf(88));
        Assert.Equal(ImGuiKey.None, EvdevImGuiKeys.KeyOf(0));
        Assert.Equal(ImGuiKey.None, EvdevImGuiKeys.KeyOf(9999));
    }

    [Fact]
    public void Every_imgui_cursor_has_an_xcursor_name()
    {
        foreach (var cursor in Enum.GetValues<ImGuiMouseCursor>())
        {
            if (cursor is ImGuiMouseCursor.Count)
            {
                continue;
            }

            var name = ImGuiCursors.NameOf(cursor);
            if (cursor is ImGuiMouseCursor.None)
            {
                Assert.Null(name);
                continue;
            }

            Assert.False(string.IsNullOrEmpty(name), $"{cursor} has no name");
        }

        Assert.Equal("text", ImGuiCursors.NameOf(ImGuiMouseCursor.TextInput));
        Assert.Equal("nwse-resize", ImGuiCursors.NameOf(ImGuiMouseCursor.ResizeNwse));
        Assert.Equal("pointer", ImGuiCursors.NameOf(ImGuiMouseCursor.Hand));
    }

    [Fact]
    public void Modifiers_come_from_the_xkb_masks_and_never_from_the_keycodes()
    {
        using var rig = new Rig();
        var ctrl = new List<bool>();
        rig.Surface.Content = new Content(_ => ctrl.Add(ImGuiApi.GetIO().KeyCtrl));

        rig.Surface.NotifyKey(1, KeyLeftCtrl, true);
        rig.Settle();
        Assert.False(ctrl[^1]);

        rig.Surface.NotifyModifiers(ImGuiModifierMasks.Standard.Control, 0, 0, 0);
        rig.Settle();
        Assert.True(ctrl[^1]);

        rig.Surface.NotifyModifiers(0, 0, 0, 0);
        rig.Surface.NotifyKey(2, KeyLeftCtrl, false);
        rig.Settle();
        Assert.False(ctrl[^1]);

        rig.Surface.Modifiers = new ImGuiModifierMasks(Control: 1u << 9, Shift: 0, Alt: 0, Super: 0);
        rig.Surface.NotifyModifiers(ImGuiModifierMasks.Standard.Control, 0, 0, 0);
        rig.Settle();
        Assert.False(ctrl[^1]);
        rig.Surface.NotifyModifiers(1u << 9, 0, 0, 0);
        rig.Settle();
        Assert.True(ctrl[^1]);
    }

    [Fact]
    public void Typed_text_reaches_a_focused_text_field_without_control_characters()
    {
        using var rig = new Rig();
        var field = new TextField();
        rig.Surface.Content = field;
        rig.Surface.KeyText = new LetterKeys();
        rig.Settle();

        rig.Type(KeyH);
        rig.Type(KeyI);
        rig.Surface.NotifyTextCommit("!\r\u0007");
        rig.Settle();

        Assert.Equal("hi!", field.Text);
        Assert.True(rig.Surface.WantsTextInput);
        Assert.True(rig.Surface.IsAnimating);
    }

    [Fact]
    public void A_click_lands_on_a_button_and_a_touch_acts_as_the_mouse()
    {
        using var rig = new Rig();
        var clicks = 0;
        var source = ImGuiMouseSource.Mouse;
        rig.Surface.Content = new Content(_ =>
        {
            Window(10, 10, 160, 80);
            if (ImGuiApi.Button("press", new Vector2(100, 30)))
            {
                clicks++;
            }

            source = ImGuiApi.GetIO().MouseSource;
            ImGuiApi.End();
        });
        rig.Settle();

        var button = ButtonCenter(rig);
        rig.Surface.NotifyPointerMotion(1, button.X, button.Y);
        rig.Settle();
        rig.Surface.NotifyPointerButton(2, BtnLeft, true);
        rig.Settle();
        rig.Surface.NotifyPointerButton(3, BtnLeft, false);
        rig.Settle();
        Assert.Equal(1, clicks);

        rig.Surface.NotifyPointerLeave();
        rig.Settle();
        rig.Surface.NotifyTouchDown(4, 7, button.X, button.Y);
        rig.Settle();
        Assert.Equal(ImGuiMouseSource.TouchScreen, source);
        rig.Surface.NotifyTouchDown(5, 8, 0, 0);
        rig.Surface.NotifyTouchUp(6, 7);
        rig.Settle();
        Assert.Equal(2, clicks);
    }

    [Fact]
    public void The_cursor_follows_what_imgui_asks_for()
    {
        using var rig = new Rig();
        rig.Surface.Content = new TextField();
        rig.Settle();
        rig.Surface.NotifyPointerMotion(1, 400, 400);
        rig.Settle();
        Assert.Equal("default", rig.Surface.CursorAt(400, 400));

        var y = 10 + FrameHeight(rig) + 12;
        rig.Surface.NotifyPointerMotion(2, 60, y);
        rig.Settle();
        Assert.Equal("text", rig.Surface.CursorAt(60, y));
    }

    [Fact]
    public void Input_passes_through_everywhere_but_a_window_or_an_open_popup()
    {
        using var rig = new Rig();
        var popup = false;
        rig.Surface.Content = new Content(_ =>
        {
            Window(20, 30, 100, 60);
            ImGuiApi.TextUnformatted("window");
            if (popup)
            {
                ImGuiApi.OpenPopup("menu");
            }

            if (ImGuiApi.BeginPopup("menu"))
            {
                ImGuiApi.TextUnformatted("item");
                ImGuiApi.EndPopup();
            }

            ImGuiApi.End();
        });
        rig.Settle();

        Assert.True(rig.Surface.AcceptsInputAt(25, 35));
        Assert.True(rig.Surface.AcceptsInputAt(119, 89));
        Assert.False(rig.Surface.AcceptsInputAt(121, 35));
        Assert.False(rig.Surface.AcceptsInputAt(400, 400));
        Assert.False(rig.Surface.AcceptsInputAt(-1, 35));

        popup = true;
        rig.Surface.Invalidate();
        rig.Settle();
        Assert.True(rig.Surface.AcceptsInputAt(400, 400));
        Assert.True(rig.Surface.IsAnimating);
    }

    [Fact]
    public void A_still_surface_stops_drawing_once_two_frames_agree()
    {
        using var rig = new Rig();
        var label = "one";
        rig.Surface.Content = new Content(_ =>
        {
            Window(10, 10, 200, 100);
            ImGuiApi.TextUnformatted(label);
            ImGuiApi.End();
        });

        rig.Settle();
        Assert.False(rig.Surface.IsDirty);
        Assert.False(rig.Surface.IsAnimating);
        Assert.Null(rig.Host.NextDueMillis);
        var renders = rig.Backend.Renders;
        var frames = rig.Surface.Frames;
        rig.Pump(10);
        Assert.Equal(frames, rig.Surface.Frames);

        rig.Surface.Invalidate();
        rig.Settle();
        Assert.Equal(renders, rig.Backend.Renders);

        label = "two";
        rig.Surface.Invalidate();
        rig.Settle();
        Assert.Equal(renders + 1, rig.Backend.Renders);
    }

    [Fact]
    public void A_held_key_and_a_refresh_request_keep_the_frames_coming()
    {
        using var rig = new Rig();
        var hz = 0;
        rig.Surface.Content = new Content(frame =>
        {
            if (hz > 0)
            {
                frame.RequestRefresh(hz);
            }
        });
        rig.Settle();

        rig.Surface.NotifyKey(1, KeyH, true);
        rig.Settle();
        Assert.True(rig.Surface.IsAnimating);
        Assert.Equal(rig.Host.FrameIntervalMillis, rig.Host.NextDueMillis);
        rig.Surface.NotifyKey(2, KeyH, false);
        rig.Settle();
        Assert.False(rig.Surface.IsAnimating);

        hz = 10;
        rig.Surface.Invalidate();
        rig.Settle();
        Assert.Equal(10, rig.Surface.RefreshHz);
        Assert.Equal(100, rig.Host.NextDueMillis);
        var frames = rig.Surface.Frames;
        rig.Clock.Advance(100);
        rig.Host.Pump();
        Assert.Equal(frames + 1, rig.Surface.Frames);
    }

    [Fact]
    public void A_frame_with_no_time_passed_still_has_a_positive_delta()
    {
        using var rig = new Rig();
        var deltas = new List<float>();
        rig.Surface.Content = new Content(frame => deltas.Add(frame.DeltaTime));
        rig.Surface.Draw();
        rig.Surface.Draw();
        rig.Surface.Draw();
        Assert.All(deltas, delta => Assert.True(delta > 0));
    }

    [Fact]
    public void A_broken_content_is_recovered_and_logged_rather_than_asserted()
    {
        using var rig = new Rig();
        var faults = 0;
        rig.Surface.Faulted += _ => faults++;
        var throws = true;
        rig.Surface.Content = new Content(_ =>
        {
            ImGuiApi.Begin("unbalanced");
            ImGuiApi.TextUnformatted("no End");
            if (throws)
            {
                throws = false;
                throw new InvalidOperationException("content failed");
            }
        });

        rig.Settle();
        rig.Surface.Invalidate();
        rig.Settle();
        Assert.Equal(1, faults);
        Assert.True(rig.Surface.Frames > 1);
        Assert.Equal(0, rig.Surface.Contexts.Context.Handle->DebugLogBuf.Buf.Size > 1 ? 1 : 0);
    }

    [Fact]
    public void The_font_atlas_reaches_the_backend_through_the_texture_flow()
    {
        using var rig = new Rig();
        rig.Surface.Content = new Content(_ =>
        {
            Window(10, 10, 200, 100);
            ImGuiApi.TextUnformatted("glyphs ABC");
            ImGuiApi.End();
        });
        rig.Settle();
        Assert.True(rig.Backend.LiveTextures >= 1);
        Assert.True(rig.Backend.Uploads >= 1);

        rig.Host.Dispose();
        Assert.True(rig.Backend.Disposed);
    }

    [Fact]
    public void Imgui_writes_no_ini_file_and_asks_for_the_texture_protocol()
    {
        using var rig = new Rig();
        using var scope = rig.Surface.Contexts.Enter();
        var io = ImGuiApi.GetIO();
        Assert.True(io.IniFilename is null);
        Assert.True(io.LogFilename is null);
        Assert.True(io.ConfigErrorRecovery);
        Assert.False(io.ConfigErrorRecoveryEnableAssert);
        Assert.True((io.BackendFlags & ImGuiBackendFlags.RendererHasTextures) != 0);
        Assert.True((io.BackendFlags & ImGuiBackendFlags.RendererHasVtxOffset) != 0);
        Assert.Equal(0, (int)(rig.Surface.Contexts.Context.Handle->DebugLogFlags & ImGuiDebugLogFlags.OutputToTty));
    }

    [Fact]
    public void The_clipboard_reads_the_selection_and_offers_imgui_copies()
    {
        using var loop = new EpollEventLoop();
        using var store = new RecordingSelection();
        using var rig = new Rig();
        rig.Surface.SetClipboard(store, loop);
        store.Offer("text/plain;charset=utf-8", "from the seat"u8.ToArray());
        for (var i = 0; i < 200 && !Clipboard(rig).Equals("from the seat"); i++)
        {
            loop.Dispatch(10);
        }

        Assert.Equal("from the seat", Clipboard(rig));

        using (rig.Surface.Contexts.Enter())
        {
            ImGuiApi.SetClipboardText("copied in imgui");
        }

        Assert.NotNull(store.Set);
        Assert.Equal("copied in imgui", Clipboard(rig));

        rig.Surface.SetClipboard(null, loop);
        Assert.Equal(string.Empty, Clipboard(rig));
    }

    private static string Clipboard(Rig rig)
    {
        using var scope = rig.Surface.Contexts.Enter();
        return ImGuiApi.GetClipboardTextS() ?? string.Empty;
    }

    private static Vector2 ButtonCenter(Rig rig) => new(10 + 8 + 50, 10 + 8 + 15 + FrameHeight(rig));

    private static float FrameHeight(Rig rig)
    {
        using var scope = rig.Surface.Contexts.Enter();
        return ImGuiApi.GetFrameHeight();
    }

    private static void Window(float x, float y, float width, float height)
    {
        ImGuiApi.SetNextWindowPos(new Vector2(x, y), ImGuiCond.Always);
        ImGuiApi.SetNextWindowSize(new Vector2(width, height), ImGuiCond.Always);
        ImGuiApi.Begin("test", ImGuiWindowFlags.NoSavedSettings);
    }

    internal sealed class Rig : IDisposable
    {
        public Rig(int width = 640, int height = 480, double scale = 1)
        {
            Backend = new NullImGuiBackend();
            Host = new ImGuiUIHost(Backend, new ImGuiHostOptions { ClockNanos = Clock.Now });
            Surface = Host.Create(new UISurfaceOptions
            {
                Target = UITargetKind.Memory,
                Width = width,
                Height = height,
                Scale = scale,
            }) ?? throw new InvalidOperationException("no surface");
            using var scope = Surface.Contexts.Enter();
            ImGuiApi.GetIO().ConfigInputTextCursorBlink = false;
        }

        public PaperSurfaceTests.FakeClock Clock { get; } = new();

        public NullImGuiBackend Backend { get; }

        public ImGuiUIHost Host { get; }

        public ImGuiUISurface Surface { get; }

        public void Type(uint key)
        {
            Surface.NotifyKey(1, key, true);
            Settle();
            Surface.NotifyKey(2, key, false);
            Settle();
        }

        public void Settle()
        {
            for (var i = 0; i < 200; i++)
            {
                if (Host.NextDueMillis is not { } due)
                {
                    return;
                }

                Clock.Advance((int)Math.Max(1, due));
                Host.Pump();
                if (Surface.WantsTextInput && !Surface.IsDirty && i > 20)
                {
                    return;
                }
            }
        }

        public void Pump(int times)
        {
            for (var i = 0; i < times; i++)
            {
                Clock.Advance(16);
                Host.Pump();
            }
        }

        public void Dispose() => Host.Dispose();
    }

    private sealed class Content(Builder build) : IImGuiContent
    {
        public void Build(ImGuiFrame frame) => build(frame);
    }

    private sealed class TextField : IImGuiContent
    {
        private readonly byte[] _buffer = new byte[64];
        private bool _focused;

        public string Text => Encoding.UTF8.GetString(_buffer, 0, Array.IndexOf(_buffer, (byte)0));

        public void Build(ImGuiFrame frame)
        {
            Window(10, 10, 300, 80);
            if (!_focused)
            {
                ImGuiApi.SetKeyboardFocusHere();
                _focused = true;
            }

            fixed (byte* label = "##field"u8)
            fixed (byte* buffer = _buffer)
            {
                ImGuiApi.InputText(label, buffer, (nuint)_buffer.Length);
            }

            ImGuiApi.End();
        }
    }

    private sealed class LetterKeys : IKeyText
    {
        public int TextFor(uint key, Span<char> into)
        {
            char? c = key switch
            {
                KeyH => 'h',
                KeyI => 'i',
                KeyEnter => '\r',
                _ => null,
            };
            if (c is null)
            {
                return 0;
            }

            into[0] = c.Value;
            return 1;
        }
    }

    private sealed class RecordingSelection : ISelectionStore, IDisposable
    {
        private readonly TestSelectionStore _inner = new();

        public DataSource? Set { get; private set; }

        public event Action<SelectionKind>? SelectionChanged
        {
            add => _inner.SelectionChanged += value;
            remove => _inner.SelectionChanged -= value;
        }

        public void Offer(string mime, byte[] data) => _inner.Offer(SelectionKind.Clipboard, (mime, data));

        public int GetOffer(SelectionKind kind, Span<string> types) => _inner.GetOffer(kind, types);

        public DataSource? Current(SelectionKind kind) => Set;

        public bool SetSelection(SelectionKind kind, DataSource? source, uint serial)
        {
            Set = source;
            return true;
        }

        public bool Receive(SelectionKind kind, string mimeType, ClientFd fd) => _inner.Receive(kind, mimeType, fd);

        public void Dispose() => _inner.Dispose();
    }
}
