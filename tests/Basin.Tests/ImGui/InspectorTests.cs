using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Inspector;
using Basin.Scene;
using Basin.UI.ImGui;
using Xunit;

namespace Basin.Tests;

public sealed class InspectorTests
{
    [Fact]
    public void Copy_cuts_on_a_character_boundary_and_marks_the_cut()
    {
        var text = new InspectorText(256);
        var source = System.Text.Encoding.UTF8.GetBytes("a" + new string('é', 150));
        Assert.Equal(301, source.Length);

        var copied = text.Copy(source);
        Assert.True(copied.Length < 256);
        Assert.True(copied.EndsWith("..."u8));
        Assert.True(System.Text.Unicode.Utf8.IsValid(copied));
        Assert.Equal(source.AsSpan(0, copied.Length - 3).ToArray(), copied[..^3].ToArray());
    }

    [Fact]
    public void Formatted_text_is_terminated_and_long_text_is_cut_with_an_ellipsis()
    {
        var text = new InspectorText(1024);
        var formatted = text.Format($"frame {42} at {1.5:0.0}");
        Assert.Equal("frame 42 at 1.5"u8.ToArray(), formatted.ToArray());
        unsafe
        {
            fixed (byte* start = formatted)
            {
                Assert.Equal(0, start[formatted.Length]);
            }
        }

        var encoded = text.Encode("日本語のタイトル");
        Assert.Equal("日本語のタイトル", System.Text.Encoding.UTF8.GetString(encoded));

        var cut = text.Encode(new string('x', 5000));
        Assert.True(cut.Length < 1024);
        Assert.True(cut.EndsWith("..."u8));

        var used = text.Used;
        text.Reset();
        Assert.True(used > 0);
        Assert.Equal(0, text.Used);
    }

    [Fact]
    public void An_open_overlay_covers_its_output_and_takes_input_only_over_its_windows()
    {
        using var rig = Rig.Start("gl");
        Assert.SkipWhen(rig is null, "no gl row and render node for the inspector");
        rig!.Overlay.Show("Windows");
        Assert.True(rig.Overlay.Open(rig.View));
        rig.Settle();

        var node = rig.Overlay.Node!;
        Assert.Equal(rig.View.Box.Width, node.Width);
        Assert.Equal(rig.View.Box.Height, node.Height);
        Assert.True(node.InputFromSurface);
        var surface = rig.Overlay.Surface!;
        Assert.True(surface.AcceptsInputAt(10, 5), "the menu bar does not take input");
        Assert.True(surface.AcceptsInputAt(600, 100), "the Windows panel does not take input");
        Assert.False(surface.AcceptsInputAt(rig.View.Box.Width - 20, rig.View.Box.Height - 20), "an empty corner takes input");

        rig.Overlay.Close();
        Assert.False(rig.Overlay.IsOpen);
        Assert.False(node.Enabled);
        Assert.False(node.InputEnabled);
        rig.Settle();
        var frames = surface.Frames;
        rig.Pump(10);
        Assert.Equal(frames, surface.Frames);
    }

    [Fact]
    public void Removing_the_overlay_output_moves_it_to_the_fallback_or_closes_it()
    {
        using var rig = Rig.Start("gl");
        Assert.SkipWhen(rig is null, "no gl row and render node for the inspector");
        Assert.True(rig!.Overlay.Open(rig.View));
        rig.Settle();

        rig.Overlay.Remove(rig.View, rig.View);
        Assert.True(rig.Overlay.IsOpen);
        Assert.Same(rig.View, rig.Overlay.Output);
        Assert.True(rig.Overlay.Node!.Enabled);

        rig.Overlay.Remove(rig.View, null);
        Assert.False(rig.Overlay.IsOpen);
        Assert.Null(rig.Overlay.Output);
        Assert.False(rig.Overlay.Node!.Enabled);

        rig.Overlay.Remove(rig.View, null);
        Assert.True(rig.Overlay.Open(rig.View));
        rig.Settle();
    }

    [Fact]
    public void Panels_are_shown_by_name_and_an_unknown_name_is_refused()
    {
        using var rig = Rig.Start("gl");
        Assert.SkipWhen(rig is null, "no gl row and render node for the inspector");
        var changes = 0;
        rig!.Overlay.Changed += () => changes++;
        Assert.False(rig.Overlay.IsShown("Scene"));
        Assert.True(rig.Overlay.Show("Scene"));
        Assert.True(rig.Overlay.IsShown("Scene"u8));
        Assert.True(rig.Overlay.Show("Scene", open: false));
        Assert.False(rig.Overlay.IsShown("Scene"));
        Assert.False(rig.Overlay.Show("Nope"));
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Golden_inspector() => AssertGolden("gl", "inspector-gl");

    [Fact]
    public void Golden_inspector_on_vulkan() => AssertGolden("vulkan", "inspector-vk");

    [Theory]
    [InlineData("gl")]
    [InlineData("vulkan")]
    public void The_library_panels_allocate_nothing_over_1000_frames(string row)
    {
        using var rig = Rig.Start(row, live: true);
        Assert.SkipWhen(rig is null, $"no {row} row and render node for the inspector");
        foreach (var name in new[] { "Outputs", "Windows", "Scene", "Memory", "Clients" })
        {
            Assert.True(rig!.Overlay.Show(name));
        }

        Assert.True(rig!.Overlay.Open(rig.View));
        var surface = rig.Overlay.Surface!;
        for (var i = 0; i < 120; i++)
        {
            rig.Round(surface);
        }

        ZeroAllocationTests.NothingAllocatedOver(1000, _ => rig.Round(surface));
        Assert.True(surface.Frames >= 1120);
    }

    private static void AssertGolden(string row, string golden)
    {
        Assert.SkipWhen(!CompositorTestHost.GoldensComparable(row), $"{row} goldens are not comparable on this driver");
        using var rig = Rig.Start(row);
        Assert.SkipWhen(rig is null, $"no {row} row and render node for the inspector");
        rig!.Overlay.Show("Windows");
        rig.Overlay.Show("Scene");
        Assert.True(rig.Overlay.Open(rig.View));
        var surface = rig.Overlay.Surface!;
        using (surface.Contexts.Enter())
        {
            Hexa.NET.ImGui.ImGui.GetIO().ConfigInputTextCursorBlink = false;
        }

        for (var i = 0; i < 4; i++)
        {
            rig.Clock.Advance(16);
            surface.Invalidate();
            surface.Draw();
        }

        Assert.True(surface.TryAcquire(out var frame));
        var buffer = frame.Buffer!;
        Assert.True(BufferCapture.TryReadRgba(buffer, rig.Renderer, out var rgba));
        Golden.AssertMatches(rgba!, buffer.Width, buffer.Height, golden, tolerance: 6);
        frame.Dispose();
    }

    internal sealed class Rig : IDisposable
    {
        private readonly BasinHost _host;
        private readonly Scene.Scene _scene;
        private readonly RenderStack _stack;
        private readonly OutputDriver _driver;
        private readonly ImGuiUIHost _imgui;
        private readonly Scene.Scene _fixedScene = new();

        private Rig(BasinHost host, Scene.Scene scene, RenderStack stack, OutputDriver driver, ImGuiUIHost imgui, bool live)
        {
            _host = host;
            _scene = scene;
            _stack = stack;
            _driver = driver;
            _imgui = imgui;
            Overlay = new InspectorOverlay(imgui, scene.Root, clockNanos: Clock.Now);
            Overlay.Add(new OutputsPanel(driver, Clock.Now));
            Overlay.Add(new WindowsPanel(new FixedToplevels()));
            var tree = new SceneTree(_fixedScene.Root);
            tree.SetPosition(40, 30);
            _ = new SceneRect(tree, 200, 120, new RenderColor(0.2f, 0.4f, 0.8f, 1f));
            _ = new SceneRect(_fixedScene.Root, 64, 64, new RenderColor(1f, 0.5f, 0f, 1f));
            Overlay.Add(new ScenePanel(live ? scene.Root : _fixedScene.Root, live ? driver : null));
            Overlay.Add(new MemoryPanel());
            _directory = new Basin.Capabilities.Defaults.DisplayClientDirectory(host.Display);
            Overlay.Add(new ClientsPanel(_directory));
        }

        private readonly Basin.Capabilities.Defaults.DisplayClientDirectory _directory;

        public PaperSurfaceTests.FakeClock Clock { get; } = new();

        public InspectorOverlay Overlay { get; }

        public OutputView View => _driver.Views[0];

        public IRenderer Renderer => _stack.Renderer;

        public static Rig? Start(string row, bool live = false)
        {
            if (!CompositorTestHost.HasWaylandServer || !File.Exists(CompositorTestHost.RenderNodePath) ||
                !CompositorTestHost.IsRunnable(row))
            {
                return null;
            }

            var host = BasinHost.Create(HostOptions.ForBackend("headless"));
            var scene = new Scene.Scene();
            var stack = Basin.Renderers.RendererCatalog.Create(row, CompositorTestHost.RenderNodePath);
            var driver = new OutputDriver(host, scene, new OutputLayout(), stack.Renderer, stack.DeviceAllocator)
            {
                HeadlessMode = new OutputMode(1280, 800, 60_000),
            };
            driver.CreateInitialOutputs();
            var clock = new PaperSurfaceTests.FakeClock();
            var imgui = Basin.Renderers.ImGuiHosts.For(
                stack.Renderer, stack.DeviceAllocator, CompositorTestHost.RenderNodePath, out _, out _,
                new ImGuiHostOptions { ClockNanos = clock.Now });
            if (imgui is null)
            {
                driver.Dispose();
                (stack.Renderer as IDisposable)?.Dispose();
                host.Dispose();
                return null;
            }

            var rig = new Rig(host, scene, stack, driver, imgui, live);
            rig.Clock.Advance(0);
            return rig;
        }

        public void Settle()
        {
            for (var i = 0; i < 200 && _imgui.NextDueMillis is { } due; i++)
            {
                Clock.Advance((int)Math.Max(1, due));
                _imgui.Pump();
            }
        }

        public void Pump(int times)
        {
            for (var i = 0; i < times; i++)
            {
                Clock.Advance(16);
                _imgui.Pump();
            }
        }

        public void Round(ImGuiUISurface surface)
        {
            Clock.Advance(16);
            surface.Invalidate();
            _imgui.Pump();
            if (surface.TryAcquire(out var frame))
            {
                frame.Dispose();
            }
        }

        public void Dispose()
        {
            Overlay.Dispose();
            _directory.Dispose();
            _imgui.Dispose();
            _driver.Dispose();
            _fixedScene.Root.Destroy();
            (_stack.Renderer as IDisposable)?.Dispose();
            _stack.DeviceAllocator?.Dispose();
            _host.Dispose();
        }
    }

    private sealed class FixedToplevels : IToplevelModel
    {
        private static readonly ToplevelInfo[] Windows =
        [
            new(1, "Terminal", "foot", ToplevelState.Activated, null, new Box(10, 10, 800, 600), Pid: 4242),
            new(2, "Notes — todo.txt", "org.gnome.TextEditor", ToplevelState.Maximized, null, new Box(0, 0, 1280, 800), Pid: 4343),
        ];

        public int Enumerate(Span<ToplevelInfo> toplevels)
        {
            if (toplevels.Length < Windows.Length)
            {
                return -1;
            }

            Windows.CopyTo(toplevels);
            return Windows.Length;
        }

        public bool TryGet(ulong toplevelId, out ToplevelInfo info)
        {
            info = Array.Find(Windows, window => window.Id == toplevelId);
            return info.Id == toplevelId;
        }

        public void AddObserver(IToplevelObserver observer)
        {
        }

        public void RemoveObserver(IToplevelObserver observer)
        {
        }

        public bool Request(ulong toplevelId, in ToplevelRequest request) => false;
    }
}
