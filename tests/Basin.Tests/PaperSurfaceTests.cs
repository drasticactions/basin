using Basin.Capabilities;
using Basin.UI.Paper;
using Prowl.PaperUI;
using Prowl.Vector;
using PaperColor = Prowl.Vector.Color;
using Xunit;

namespace Basin.Tests;

public sealed class PaperSurfaceTests
{
    private const uint BtnLeft = 0x110;
    private const uint KeyH = 35;
    private const uint KeyI = 23;
    private const uint KeyBackspace = 14;

    public static TheoryData<string, string> Rows => new()
    {
        { "gl", "gl" },
        { "vulkan", "vulkan" },
    };

    [Fact]
    public void Every_paper_cursor_has_an_xcursor_name()
    {
        var expected = new Dictionary<PaperCursor, string>
        {
            [PaperCursor.Inherit] = "default",
            [PaperCursor.Default] = "default",
            [PaperCursor.Pointer] = "pointer",
            [PaperCursor.Grab] = "grab",
            [PaperCursor.Grabbing] = "grabbing",
            [PaperCursor.Text] = "text",
            [PaperCursor.Crosshair] = "crosshair",
            [PaperCursor.ResizeHorizontal] = "ew-resize",
            [PaperCursor.ResizeVertical] = "ns-resize",
            [PaperCursor.ResizeNWSE] = "nwse-resize",
            [PaperCursor.ResizeNESW] = "nesw-resize",
            [PaperCursor.ResizeAll] = "move",
            [PaperCursor.NotAllowed] = "not-allowed",
            [PaperCursor.Wait] = "wait",
            [PaperCursor.Help] = "help",
        };

        foreach (var cursor in Enum.GetValues<PaperCursor>())
        {
            Assert.True(expected.TryGetValue(cursor, out var name), $"{cursor} has no expected name");
            Assert.Equal(name, PaperCursors.NameOf(cursor));
        }
    }

    [Fact]
    public void Evdev_keys_map_to_the_paper_keys_named_for_their_position()
    {
        Assert.Equal(PaperKey.A, EvdevPaperKeys.KeyOf(30));
        Assert.Equal(PaperKey.Enter, EvdevPaperKeys.KeyOf(28));
        Assert.Equal(PaperKey.Escape, EvdevPaperKeys.KeyOf(1));
        Assert.Equal(PaperKey.Tab, EvdevPaperKeys.KeyOf(15));
        Assert.Equal(PaperKey.Left, EvdevPaperKeys.KeyOf(105));
        Assert.Equal(PaperKey.LeftControl, EvdevPaperKeys.KeyOf(29));
        Assert.Equal(PaperKey.Unknown, EvdevPaperKeys.KeyOf(0));
        Assert.Equal(PaperKey.Unknown, EvdevPaperKeys.KeyOf(9999));
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_paper_button_takes_a_click_through_the_surface(string row, string backend)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        CompositorTestHost.SkipUnlessRunnable(backend);
        using var host = new CompositorTestHost(renderer: row);
        using var lease = QuillHostTests.QuillLease.Require(host, backend);
        var clock = new FakeClock();
        using var paper = new PaperUIHost(lease.Host, clockNanos: clock.Now);
        var surface = Create(paper, 200, 100);
        var clicks = 0;
        surface.Build = p =>
        {
            using (p.Column("root").Size(p.Stretch()).Padding(20).Enter())
            {
                p.Box("button").Size(80, 30).BackgroundColor(PaperColor.Blue).OnClick(_ => clicks++);
            }
        };

        Settle(paper, clock);
        surface.NotifyPointerEnter(60, 35);
        surface.NotifyPointerButton(1, BtnLeft, true);
        surface.NotifyPointerButton(2, BtnLeft, false);
        Settle(paper, clock);

        Assert.Equal(1, clicks);

        surface.NotifyPointerMotion(3, 150, 80);
        surface.NotifyPointerButton(4, BtnLeft, true);
        surface.NotifyPointerButton(5, BtnLeft, false);
        Settle(paper, clock);

        Assert.Equal(1, clicks);
        surface.Dispose();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_paper_text_field_takes_focus_and_the_text_of_each_key(string row, string backend)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        CompositorTestHost.SkipUnlessRunnable(backend);
        using var host = new CompositorTestHost(renderer: row);
        using var lease = QuillHostTests.QuillLease.Require(host, backend);
        var clock = new FakeClock();
        using var paper = new PaperUIHost(lease.Host, clockNanos: clock.Now);
        var surface = Create(paper, 240, 80);
        surface.KeyText = new LetterKeys();
        var face = Basin.Frames.Quill.QuillFrameFonts.Bundled();
        var value = string.Empty;
        surface.Build = p =>
        {
            using (p.Column("root").Size(p.Stretch()).Padding(10).Enter())
            {
                p.Box("field").Size(200, 24).FontSize(14).BackgroundColor(PaperColor.DimGray)
                    .TextField(value, face, text => value = text);
            }
        };

        Settle(paper, clock);
        Assert.False(surface.WantsTextInput);

        surface.NotifyPointerEnter(50, 20);
        surface.NotifyPointerButton(1, BtnLeft, true);
        surface.NotifyPointerButton(2, BtnLeft, false);
        Settle(paper, clock);
        Assert.True(surface.WantsTextInput);

        Type(surface, paper, clock, KeyH);
        Type(surface, paper, clock, KeyI);
        Assert.Equal("hi", value);

        surface.NotifyTextCommit("!");
        Settle(paper, clock);
        Assert.Equal("hi!", value);

        Type(surface, paper, clock, KeyBackspace);
        Assert.Equal("hi", value);
        Assert.Equal("text", surface.CursorAt(50, 20));
        surface.Dispose();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_click_that_closes_the_surface_finishes_the_frame_first(string row, string backend)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        CompositorTestHost.SkipUnlessRunnable(backend);
        using var host = new CompositorTestHost(renderer: row);
        using var lease = QuillHostTests.QuillLease.Require(host, backend);
        var clock = new FakeClock();
        using var paper = new PaperUIHost(lease.Host, clockNanos: clock.Now);
        var scene = new Basin.Scene.Scene();
        var textures = lease.TextureCount;
        var node = new Basin.Scene.UISurfaceNode(scene.Root, paper) { Target = UITargetKind.Dmabuf };
        Assert.True(node.Configure(200, 100, 1.0));
        var surface = (PaperSurface)node.Surface!;
        surface.Build = p =>
        {
            using (p.Column("root").Size(p.Stretch()).Padding(20).Enter())
            {
                p.Box("close").Size(80, 30).BackgroundColor(PaperColor.Red).OnClick(_ => node.Dispose());
            }
        };

        Settle(paper, clock);
        surface.NotifyPointerEnter(60, 35);
        surface.NotifyPointerButton(1, BtnLeft, true);
        surface.NotifyPointerButton(2, BtnLeft, false);
        Settle(paper, clock);

        Assert.Empty(paper.Surfaces);
        Assert.Equal(textures, lease.TextureCount);
        scene.Root.Destroy();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_redraw_never_lands_in_the_buffer_the_scene_is_showing(string row, string backend)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        CompositorTestHost.SkipUnlessRunnable(backend);
        using var host = new CompositorTestHost(renderer: row);
        using var lease = QuillHostTests.QuillLease.Require(host, backend);
        var clock = new FakeClock();
        using var paper = new PaperUIHost(lease.Host, clockNanos: clock.Now);
        var scene = new Basin.Scene.Scene();
        var node = new Basin.Scene.UISurfaceNode(scene.Root, paper) { Target = UITargetKind.Dmabuf };
        Assert.True(node.Configure(200, 100, 1.0));
        var surface = (PaperSurface)node.Surface!;
        surface.Build = p =>
        {
            using (p.Column("root").Size(p.Stretch()).Padding(20).Enter())
            {
                p.Box("button").Size(80, 30).BackgroundColor(PaperColor.Blue).Hovered.BackgroundColor(PaperColor.Red).End();
            }
        };

        Settle(paper, clock);
        var seen = new HashSet<IBuffer>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < 12; i++)
        {
            var shown = node.Node.Buffer!;
            using var held = new UIFrame(shown.Lock(), damage: null);
            seen.Add(shown);
            surface.NotifyPointerMotion((uint)i, (i & 1) == 0 ? 60 : 150, 35);
            Assert.True(surface.Draw());
            Assert.NotSame(shown, node.Node.Buffer);
        }

        seen.Add(node.Node.Buffer!);
        Assert.InRange(seen.Count, 2, 4);
        node.Dispose();
        scene.Root.Destroy();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void An_idle_paper_surface_draws_nothing(string row, string backend)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        CompositorTestHost.SkipUnlessRunnable(backend);
        using var host = new CompositorTestHost(renderer: row);
        using var lease = QuillHostTests.QuillLease.Require(host, backend);
        var clock = new FakeClock();
        using var paper = new PaperUIHost(lease.Host, clockNanos: clock.Now);
        var surface = Create(paper, 200, 100);
        surface.Build = p =>
        {
            using (p.Column("root").Size(p.Stretch()).Padding(20).Enter())
            {
                p.Box("button").Size(80, 30).BackgroundColor(PaperColor.Blue)
                    .Hovered.BackgroundColor(PaperColor.Red).End()
                    .Transition(GuiProp.BackgroundColor, 0.2f);
            }
        };

        Settle(paper, clock);
        var settled = surface.Frames;
        for (var i = 0; i < 60; i++)
        {
            clock.Advance(16);
            Assert.Null(paper.NextDueMillis);
            paper.Pump();
        }

        Assert.Equal(settled, surface.Frames);

        surface.NotifyPointerEnter(60, 35);
        var frames = Settle(paper, clock);
        Assert.True(frames > 2, $"a 0.2 s transition settled in {frames} frames");
        Assert.Null(paper.NextDueMillis);
        surface.Dispose();
    }

    [Fact]
    public void The_paper_clipboard_serves_its_own_text_and_reads_what_another_owner_sets()
    {
        using var host = new CompositorTestHost();
        var store = new Basin.Seat.SeatSelectionStore(host.Seat);
        using var clipboard = new PaperClipboard(store, host.Loop);
        Assert.Equal(string.Empty, clipboard.GetClipboardText());

        clipboard.SetClipboardText("from paper");
        Assert.Equal("from paper", clipboard.GetClipboardText());
        Assert.True(store.GetOffer(SelectionKind.Clipboard, new string[8]) > 0);

        var bytes = System.Text.Encoding.UTF8.GetBytes("from a client");
        var source = new DataSource(
            ["text/plain;charset=utf-8"],
            (_, fd) =>
            {
                using var stream = new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(fd.Value, ownsHandle: true), FileAccess.Write);
                stream.Write(bytes);
            });
        store.SetSelection(SelectionKind.Clipboard, source, SelectionSerial.Unchecked);
        host.PumpUntil(() => clipboard.GetClipboardText() == "from a client");

        Assert.Equal("from a client", clipboard.GetClipboardText());
    }

    internal static PaperSurface Create(PaperUIHost paper, int width, int height) =>
        paper.Create(new UISurfaceOptions
        {
            Target = UITargetKind.Dmabuf,
            Width = width,
            Height = height,
            Scale = 1.0,
        })!;

    internal static int Settle(PaperUIHost paper, FakeClock clock)
    {
        var frames = 0;
        for (var i = 0; i < 200 && paper.NextDueMillis is not null; i++)
        {
            clock.Advance(16);
            paper.Pump();
            frames++;
        }

        Assert.Null(paper.NextDueMillis);
        return frames;
    }

    private static void Type(PaperSurface surface, PaperUIHost paper, FakeClock clock, uint key)
    {
        surface.NotifyKey(1, key, true);
        surface.NotifyKey(2, key, false);
        Settle(paper, clock);
    }

    internal sealed class FakeClock
    {
        private long _nanos = 1_000_000_000;

        public long Now() => _nanos;

        public void Advance(int millis) => _nanos += millis * 1_000_000L;
    }

    private sealed class LetterKeys : IKeyText
    {
        public int TextFor(uint key, Span<char> into)
        {
            char? c = key switch
            {
                KeyH => 'h',
                KeyI => 'i',
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
}
