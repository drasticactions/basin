using Basin.Sdl;
using SDL3;
using Xunit;

namespace SdlComp.Tests;

public sealed class SdlOutputsTests
{
    [Fact]
    public void A_second_window_is_a_second_output_to_the_right()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster, outputs: 2);
        var windows = harness.Comp.OutputWindows;
        Assert.Equal(2, windows.Count);
        Assert.Equal(2, harness.Client.Outputs.Count);
        Assert.Equal(["shell", "SDL-2"], harness.Host.Screens.AdvertisedKeys);
        var second = windows[1].View;
        Assert.Equal(320, harness.Host.Layout.BoxOf(second.Output).X);
        Assert.Equal(320, second.SceneOutput.Position.X);

        SdlEvents.Motion(windows[1].Window.Id, 15, 25);
        harness.PumpUntil(() => harness.Comp.Shell.PointerPosition == (335.0, 25.0), "the second window's pointer never reached the shell");
    }

    [Fact]
    public void Closing_the_second_window_removes_its_output()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster, outputs: 2);
        var second = harness.Comp.OutputWindows[1];
        SdlEvents.Window(second.Window.Id, SDL.EventType.WindowCloseRequested);
        harness.PumpUntil(() => harness.Comp.OutputWindows.Count == 1, "the second window never closed");
        Assert.Equal(["shell"], harness.Host.Screens.AdvertisedKeys);
        Assert.False(harness.Comp.Loop.QuitRequested);
        Assert.Single(harness.Comp.FrameLoop.Windows);
    }

    [Fact]
    public void Closing_the_shell_window_quits()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster, outputs: 2);
        SdlEvents.Window(harness.Comp.Window.Window.Id, SDL.EventType.WindowCloseRequested);
        harness.PumpUntil(() => harness.Comp.Loop.QuitRequested, "closing the shell window never quit");
    }

    [Fact]
    public void Minimizing_every_window_suspends_the_host_and_restoring_one_resumes_it()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster, outputs: 2);
        var windows = harness.Comp.OutputWindows;
        SdlEvents.Window(windows[0].Window.Id, SDL.EventType.WindowMinimized);
        harness.Pump();
        Assert.False(harness.Host.IsSuspended);
        SdlEvents.Window(windows[1].Window.Id, SDL.EventType.WindowMinimized);
        harness.Step();
        Assert.True(harness.Host.IsSuspended);
        SdlEvents.Window(windows[1].Window.Id, SDL.EventType.WindowRestored);
        harness.Step();
        Assert.False(harness.Host.IsSuspended);
    }

    [Fact]
    public void The_window_refresh_reaches_the_output_mode()
    {
        using var harness = new SdlCompHarness(SdlRenderDevices.Raster);
        var window = harness.Comp.Window;
        Assert.SkipWhen(window.RefreshRate <= 0, "this SDL driver reports no refresh rate");
        Assert.Equal((int)Math.Round(window.RefreshRate * 1000), window.View.Output.CurrentMode.RefreshMilliHz);
        Assert.Equal(TimeSpan.FromSeconds(1.0 / window.RefreshRate), harness.Comp.FrameLoop.FrameInterval);
    }
}
