using System.Numerics;
using System.Text.Unicode;
using Basin.Capabilities;
using Basin.UI.ImGui;
using Hexa.NET.ImGui;
using Hexa.NET.ImPlot;
using Xunit;
using ImGuiApi = Hexa.NET.ImGui.ImGui;

namespace Basin.Tests;

public sealed partial class ZeroAllocationTests
{
    public static TheoryData<string, string> ImGuiRows => new()
    {
        { "gl", "gl" },
        { "skia-gl", "gl" },
        { "vulkan", "vulkan" },
        { "skia-vulkan", "vulkan" },
    };

    [Theory]
    [MemberData(nameof(ImGuiRows))]
    public void An_idle_imgui_surface_allocates_nothing_over_1000_frames(string row, string backend)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        using var host = new CompositorTestHost(renderer: row);
        using var lease = ImGuiHostTests.ImGuiLease.Open(host, backend);
        Assert.SkipWhen(lease is null, $"{row} builds no {backend} imgui host");
        var surface = lease!.Create(320, 240);
        surface.Content = new LiveContent { Still = true };
        for (var i = 0; i < 60; i++)
        {
            IdleRound(lease, surface);
        }

        Assert.False(surface.IsAnimating);
        var frames = surface.Frames;
        NothingAllocated(1000, _ => IdleRound(lease, surface));
        Assert.Equal(frames, surface.Frames);
        surface.Dispose();
    }

    [Theory]
    [MemberData(nameof(ImGuiRows))]
    public void A_drawing_imgui_surface_allocates_nothing_over_1000_frames(string row, string backend)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        using var host = new CompositorTestHost(renderer: row);
        using var lease = ImGuiHostTests.ImGuiLease.Open(host, backend);
        Assert.SkipWhen(lease is null, $"{row} builds no {backend} imgui host");
        var surface = lease!.Create(640, 480);
        surface.Content = new LiveContent();
        for (var i = 0; i < 60; i++)
        {
            DrawRound(lease, surface);
        }

        var renders = lease.Host.Surfaces[0].Renders;
        NothingAllocated(1000, _ => DrawRound(lease, surface));
        Assert.True(surface.Renders >= renders + 1000, "the drawing rows did not render every frame");
        surface.Dispose();
    }

    [Fact]
    public void Seat_input_into_an_imgui_surface_allocates_nothing()
    {
        using var rig = new ImGuiSurfaceTests.Rig();
        rig.Surface.Content = new LiveContent { Still = true };
        var pressed = new uint[] { 30, 31 };
        var text = "漢字".ToCharArray();
        for (var i = 0; i < 60; i++)
        {
            DeliverImGui(rig, pressed, text, i);
        }

        NothingAllocated(1000, i => DeliverImGui(rig, pressed, text, i));
    }

    internal static void NothingAllocatedOver(int rounds, Action<int> round) => NothingAllocated(rounds, round);

    private static void IdleRound(ImGuiHostTests.ImGuiLease lease, ImGuiUISurface surface)
    {
        lease.Clock.Advance(16);
        _ = lease.Host.NextDueMillis;
        lease.Host.Pump();
        if (surface.TryAcquire(out var frame))
        {
            frame.Dispose();
        }
    }

    private static void DrawRound(ImGuiHostTests.ImGuiLease lease, ImGuiUISurface surface)
    {
        lease.Clock.Advance(16);
        surface.Invalidate();
        lease.Host.Pump();
        if (surface.TryAcquire(out var frame))
        {
            frame.Dispose();
        }
    }

    private static void DeliverImGui(ImGuiSurfaceTests.Rig rig, uint[] pressed, char[] text, int i)
    {
        var surface = rig.Surface;
        var time = (uint)i;
        surface.NotifyPointerEnter(i % 64, i % 48);
        surface.NotifyPointerMotion(time, (i + 3) % 64, i % 48);
        surface.NotifyPointerButton(time, 0x110, pressed: true);
        surface.NotifyPointerButton(time, 0x110, pressed: false);
        surface.NotifyPointerAxis(time, 0, 15);
        surface.NotifyPointerLeave();
        surface.NotifyKeyboardEnter(pressed);
        surface.NotifyModifiers((uint)(i & 7), 0, 0, 0);
        surface.NotifyKey(time, 30, pressed: true);
        surface.NotifyKey(time, 30, pressed: false);
        surface.NotifyKeyboardLeave();
        surface.NotifyTouchDown(time, 0, i % 32, i % 16);
        surface.NotifyTouchMotion(time, 0, (i + 1) % 32, i % 16);
        surface.NotifyTouchUp(time, 0);
        surface.NotifyTouchCancel();
        surface.NotifyPreedit(text, 0, text.Length);
        surface.NotifyTextCommit(text);
        _ = surface.WantsTextInput;
        _ = surface.CursorAt(i % 64, i % 48);
        _ = surface.AcceptsInputAt(i % 64, i % 48);
        rig.Clock.Advance(16);
        surface.Draw();
    }

    internal sealed unsafe class LiveContent : IImGuiContent
    {
        private readonly byte[] _text = new byte[64];
        private readonly float[] _samples = new float[120];
        private int _frame;

        public bool Still { get; init; }

        public bool NoDemo { get; init; }

        public bool NoPlot { get; init; }

        public bool NoText { get; init; }

        public void Build(ImGuiFrame frame)
        {
            if (!Still)
            {
                _frame++;
            }

            ImGuiApi.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.Always);
            ImGuiApi.SetNextWindowSize(new Vector2(300, 220), ImGuiCond.Always);
            ImGuiApi.Begin("live"u8);
            if (!NoText)
            {
            _ = Utf8.TryWrite(_text.AsSpan(0, _text.Length - 1), $"frame {_frame} at {frame.DeltaTime:F4}", out var written);
            _text[written] = 0;
            fixed (byte* text = _text)
            {
                ImGuiApi.TextUnformatted(text);
            }
            }

            ImGuiApi.ProgressBar((_frame % 100) / 100f, new Vector2(-1, 0), "progress"u8);
            for (var i = 0; i < _samples.Length; i++)
            {
                _samples[i] = MathF.Sin((i + _frame) / 10f);
            }

            if (!NoPlot && ImPlot.BeginPlot("samples"u8, new Vector2(-1, 120)))
            {
                fixed (float* samples = _samples)
                {
                    ImPlot.PlotLine("sin"u8, samples, _samples.Length);
                }

                ImPlot.EndPlot();
            }

            ImGuiApi.End();
            if (!Still && !NoDemo)
            {
                ImGuiApi.ShowDemoWindow();
            }
        }
    }
}
