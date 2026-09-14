using System.Reflection;
using Basin.Capabilities;
using Basin.Config;
using Basin.Diagnostics;
using Basin.Portal;
using Basin.Portal.Prompts.Skia;
using Basin.Render.Skia;
using Basin.UI.Skia;
using SkiaSharp;
using Xunit;

namespace Basin.Tests;

public sealed class SkiaPromptTests
{
    private sealed class TestPromptSurface : ISkiaPromptSurface
    {
        private readonly ISkiaUISurface _inner;
        private readonly RecordingPromptHost _host;
        private bool _disposed;

        public TestPromptSurface(RecordingPromptHost host, ISkiaUISurface inner, SkiaPromptView view)
        {
            _host = host;
            _inner = inner;
            View = view;
            Paint();
        }

        public SkiaPromptView View { get; }

        public UISurfaceSize Size => _inner.Size;

        public bool IsDisposed => _disposed;

        public bool TryAcquire(out UIFrame frame) => _inner.TryAcquire(out frame);

        public void NotifyPointerEnter(double x, double y) => Redraw(View.PointerMove(x, y));

        public void NotifyPointerMotion(uint timeMs, double x, double y) => Redraw(View.PointerMove(x, y));

        public void NotifyPointerButton(uint timeMs, uint button, bool pressed) => Redraw(View.PointerButton(button, pressed));

        public void NotifyPointerAxis(uint timeMs, double dx, double dy) => Redraw(View.PointerAxis(dx, dy));

        public void NotifyPointerLeave() => Redraw(View.PointerLeave());

        public void NotifyKey(uint timeMs, uint key, bool pressed) => Redraw(View.Key(key, pressed));

        public void NotifyModifiers(uint depressed, uint latched, uint locked, uint group) =>
            View.Modifiers(depressed, latched, locked, group);

        public void Repaint() => Paint();

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _host.Track(this);
            _inner.Dispose();
        }

        private void Paint()
        {
            if (_disposed)
            {
                return;
            }

            var size = _inner.Size;
            var canvas = _inner.BeginDraw();
            try
            {
                View.Paint(canvas, size.Width, size.Height);
            }
            finally
            {
                _inner.EndDraw();
            }
        }

        private void Redraw(bool dirty)
        {
            if (dirty)
            {
                Paint();
            }
        }
    }

    private sealed class RecordingPromptHost : ISkiaPromptHost
    {
        private readonly SkiaUIHost _host;
        private readonly IOutput _output;

        public RecordingPromptHost(SkiaUIHost host, IOutput output)
        {
            _host = host;
            _output = output;
        }

        public List<TestPromptSurface> Shown { get; } = [];

        public List<TestPromptSurface> Hidden { get; } = [];

        public IOutput? OutputFor(string parentWindow) => _output;

        public ISkiaPromptSurface? Show(SkiaPromptView view, IOutput output, bool cover)
        {
            Assert.Same(_output, output);
            var target = (_host.Produces & UITargetKind.Memory) != 0 ? UITargetKind.Memory : _host.Produces;
            var created = _host.CreateSurface(new UISurfaceOptions
            {
                Target = target,
                Width = view.SurfaceWidth,
                Height = view.Height,
                Scale = _output.Scale,
            });
            var surface = new TestPromptSurface(this, (ISkiaUISurface)created!, view);
            Shown.Add(surface);
            return surface;
        }

        public TestPromptSurface Current => Shown[^1];

        public void Track(TestPromptSurface surface) => Hidden.Add(surface);
    }

    private sealed class Rig : IDisposable
    {
        public Rig(int width = 160, int height = 120)
        {
            Host = new CompositorTestHost(width, height);
            UIHost = new SkiaUIHost();
            PromptHost = new RecordingPromptHost(UIHost, Host.Output);
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NotoSansCJK-Regular.ttc")!;
            using var data = SKData.Create(stream);
            Typeface = SkiaTypefaces.FromCollection(data, "Noto Sans CJK JP") ?? throw new InvalidOperationException("no JP face");
            Prompts = new SkiaPortalPrompts(PromptHost, Host.Layout) { Typeface = Typeface };
        }

        public CompositorTestHost Host { get; }

        public SkiaUIHost UIHost { get; }

        public RecordingPromptHost PromptHost { get; }

        public SKTypeface Typeface { get; }

        public SkiaPortalPrompts Prompts { get; }

        public MemoryBuffer Snapshot(TestPromptSurface surface)
        {
            Assert.True(surface.TryAcquire(out var frame));
            using (frame)
            {
                var source = Assert.IsType<MemoryBuffer>(frame.Buffer);
                var copy = new MemoryBuffer(source.Width, source.Height, DrmFormat.Argb8888);
                Assert.True(source.BeginDataAccess(BufferDataAccess.Read, out var from));
                Assert.True(copy.BeginDataAccess(BufferDataAccess.Write, out var to));
                unsafe
                {
                    for (var y = 0; y < source.Height; y++)
                    {
                        new ReadOnlySpan<byte>((byte*)from.Data + y * from.Stride, source.Width * 4).CopyTo(new Span<byte>((byte*)to.Data + y * to.Stride, source.Width * 4));
                    }
                }

                copy.EndDataAccess();
                source.EndDataAccess();
                return copy;
            }
        }

        public void Click(TestPromptSurface surface, double x, double y)
        {
            surface.NotifyPointerEnter(x, y);
            surface.NotifyPointerMotion(1, x, y);
            surface.NotifyPointerButton(2, InputCodes.BtnLeft, true);
            surface.NotifyPointerButton(3, InputCodes.BtnLeft, false);
        }

        public (double X, double Y) Button(TestPromptSurface surface, int fromRight)
        {
            var size = surface.Size;
            var x = size.Width - SkiaPromptView.Padding - (fromRight + 1) * SkiaPromptView.ButtonWidth - fromRight * 10 + SkiaPromptView.ButtonWidth / 2.0;
            var y = size.Height - SkiaPromptView.Padding - SkiaPromptView.ButtonHeight / 2.0;
            return (x, y);
        }

        public void Dispose()
        {
            Prompts.Dispose();
            SkiaCensus.Release(Typeface);
            UIHost.Dispose();
            Host.Dispose();
        }
    }

    private static void Golden(Rig rig, TestPromptSurface surface, string name)
    {
        var shot = rig.Snapshot(surface);
        try
        {
            Tests.Golden.AssertMatches(shot, name);
        }
        finally
        {
            shot.Destroy();
        }
    }

    [Fact]
    public void The_app_line_names_the_requester_three_ways()
    {
        Assert.Equal("An unknown application is asking", SkiaPortalPrompts.AppLine("", ""));
        Assert.Equal("org.example.App is asking", SkiaPortalPrompts.AppLine("org.example.App", ""));
        Assert.Equal("Example (org.example.App) is asking", SkiaPortalPrompts.AppLine("org.example.App", "Example"));
    }

    [Fact]
    public async Task The_source_picker_paints_lists_and_shares_the_row_clicked()
    {
        using var rig = new Rig();
        var prompt = new SourcePrompt("org.example.App", "", true, PromptSourceKinds.Monitor | PromptSourceKinds.Window, false, true,
            [new PromptOutput(rig.Host.Output, "HEADLESS-1", "Headless", new Box(0, 0, 160, 120), 1)],
            [new PromptToplevel(7, "Editor", "org.example.Editor"), new PromptToplevel(8, "Terminal", "org.example.Terminal")]);
        var task = rig.Prompts.SelectSources(prompt, CancellationToken.None);
        var surface = rig.PromptHost.Current;
        Assert.Equal((SkiaPromptView.Width, 1.0), (surface.Size.Width, surface.Size.Scale));
        Golden(rig, surface, "prompt-sources");

        rig.Click(surface, SkiaPromptView.Padding + 40, SkiaPromptView.Padding + 48 + SkiaPromptView.RowHeight * 2 + 10);
        rig.Click(surface, SkiaPromptView.Padding + 40, SkiaPromptView.Padding + 48 + SkiaPromptView.RowHeight * 3 + 10);
        var (x, y) = rig.Button(surface, 0);
        rig.Click(surface, x, y);
        Assert.True(task.IsCompleted);
        var outcome = await task;
        Assert.True(outcome.IsAccepted);
        var chosen = Assert.Single(outcome.Value.Sources);
        Assert.Equal((PromptSourceKinds.Window, 8UL), (chosen.Kind, chosen.ToplevelId));
        Assert.Equal(2u, outcome.Value.PersistMode);
        Assert.Single(rig.PromptHost.Hidden);
        Assert.Equal(0, rig.Prompts.Open);
    }

    [Fact]
    public async Task The_device_picker_toggles_rows_and_escape_cancels()
    {
        using var rig = new Rig();
        var prompt = new DevicePrompt("org.example.App", "", true, InputDeviceCapability.Keyboard | InputDeviceCapability.Pointer, true, false);
        var task = rig.Prompts.SelectDevices(prompt, CancellationToken.None);
        var surface = rig.PromptHost.Current;
        Golden(rig, surface, "prompt-devices");

        rig.Click(surface, SkiaPromptView.Padding + 20, SkiaPromptView.Padding + 48 + 10);
        var (x, y) = rig.Button(surface, 0);
        rig.Click(surface, x, y);
        Assert.True(task.IsCompleted);
        var devices = await task;
        Assert.Equal((InputDeviceCapability.Pointer, true), (devices.Value.Devices, devices.Value.Clipboard));

        var second = rig.Prompts.SelectDevices(prompt, CancellationToken.None);
        var again = rig.PromptHost.Current;
        again.NotifyKey(1, InputCodes.KeyEsc, true);
        Assert.True(second.IsCompleted);
        Assert.Equal(PromptResponse.Cancelled, (await second).Response);
        Assert.Equal(2, rig.PromptHost.Hidden.Count);
    }

    [Fact]
    public async Task The_confirmation_answers_deny_and_allow_and_a_closed_request_cancels()
    {
        using var rig = new Rig();
        var prompt = new ConfirmPrompt("org.example.App", "", true, "Take a screenshot?", "org.example.App wants to take a screenshot");
        var task = rig.Prompts.Confirm(prompt, CancellationToken.None);
        var surface = rig.PromptHost.Current;
        Golden(rig, surface, "prompt-confirm");
        var (x, y) = rig.Button(surface, 1);
        rig.Click(surface, x, y);
        Assert.Equal(PromptResponse.Denied, (await task).Response);

        task = rig.Prompts.Confirm(prompt, CancellationToken.None);
        rig.PromptHost.Current.NotifyKey(1, InputCodes.KeyEnter, true);
        Assert.True((await task).IsAccepted);

        using var cancellation = new CancellationTokenSource();
        task = rig.Prompts.Confirm(prompt, cancellation.Token);
        Assert.Equal(1, rig.Prompts.Open);
        cancellation.Cancel();
        Assert.True(task.IsCompleted);
        Assert.Equal(PromptResponse.Cancelled, (await task).Response);
        Assert.Equal(0, rig.Prompts.Open);
        Assert.Equal(3, rig.PromptHost.Hidden.Count);
    }

    [Fact]
    public async Task The_area_picker_covers_the_output_and_returns_the_drag_in_layout_coordinates()
    {
        using var rig = new Rig();
        rig.Host.Layout.Move(rig.Host.Output, 100, 50);
        var prompt = new AreaPrompt("org.example.App", "", true, null, false);
        var task = rig.Prompts.SelectArea(prompt, CancellationToken.None);
        var surface = rig.PromptHost.Current;
        Assert.Equal((160, 120), (surface.Size.Width, surface.Size.Height));
        surface.NotifyPointerEnter(20, 30);
        surface.NotifyPointerMotion(1, 20, 30);
        surface.NotifyPointerButton(2, InputCodes.BtnLeft, true);
        surface.NotifyPointerMotion(3, 60, 70);
        Golden(rig, surface, "prompt-area");
        surface.NotifyPointerButton(4, InputCodes.BtnLeft, false);
        Assert.True(task.IsCompleted);
        Assert.Equal(new Box(120, 80, 40, 40), (await task).Value);

        task = rig.Prompts.SelectArea(prompt with { PickPoint = true }, CancellationToken.None);
        var picker = rig.PromptHost.Current;
        rig.Click(picker, 15.5, 25.5);
        Assert.Equal(new Box(115, 75, 1, 1), (await task).Value);

        task = rig.Prompts.SelectArea(prompt, CancellationToken.None);
        rig.PromptHost.Current.NotifyKey(1, InputCodes.KeyEnter, true);
        Assert.Equal(new Box(100, 50, 160, 120), (await task).Value);
    }

    [Fact]
    public async Task The_shortcut_prompt_captures_a_chord_from_the_keymap()
    {
        using var rig = new Rig();
        rig.Host.Seat.Keyboard.SetKeymap(new KeymapNames(Layout: "us"));
        Assert.SkipWhen(rig.Host.Seat.Keyboard.Keymap is null, "no xkb keymap could be compiled on this host");
        rig.Prompts.Keymap = rig.Host.Seat.Keyboard;
        var prompt = new ShortcutPrompt("org.example.App", "", true,
        [
            new ShortcutPromptRow("record", "Start recording", "CTRL+SHIFT+r", true, ""),
            new ShortcutPromptRow("stop", "Stop recording", "", false, "CTRL+ALT+s"),
        ]);
        var task = rig.Prompts.BindShortcuts(prompt, CancellationToken.None);
        var surface = rig.PromptHost.Current;
        Golden(rig, surface, "prompt-shortcuts");

        rig.Click(surface, SkiaPromptView.Padding + 40, SkiaPromptView.Padding + 48 + 10);
        Assert.True(Assert.IsType<ShortcutPromptView>(surface.View).IsCapturing);
        surface.NotifyModifiers((uint)(Modifiers.Super | Modifiers.Shift), 0, 0, 0);
        surface.NotifyKey(1, InputCodes.KeyF9, true);
        surface.NotifyModifiers(0, 0, 0, 0);
        Assert.False(Assert.IsType<ShortcutPromptView>(surface.View).IsCapturing);
        var (x, y) = rig.Button(surface, 0);
        rig.Click(surface, x, y);
        Assert.True(task.IsCompleted);
        var bindings = (await task).Value!;
        Assert.Equal(2, bindings.Length);
        Assert.Equal(("record", "SHIFT+LOGO+F9"), (bindings[0].Id, bindings[0].Trigger));
        Assert.Equal(("stop", "CTRL+ALT+s"), (bindings[1].Id, bindings[1].Trigger));
    }
}
