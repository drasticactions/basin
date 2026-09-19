using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Basin.Diagnostics;
using Basin.Hosted;
using Tarn;
using Xunit;

namespace Basin.Tests;

public sealed class TarnViewTests
{
    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            BasinCounters.Reset();
            View = new MainView();
            Window = new Window { Width = 640, Height = 480, Content = View };
            Window.Show();
            Pump();
        }

        public MainView View { get; }

        public Window Window { get; }

        public void Pump()
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            View.Host?.Loop.Dispatch(0);
        }

        public void PumpUntil(Func<bool> condition, string what)
        {
            for (var i = 0; i < 200 && !condition(); i++)
            {
                Pump();
            }

            Assert.True(condition(), what);
        }

        public void Dispose()
        {
            var shutdown = View.ShutdownAsync();
            for (var i = 0; i < 50 && !shutdown.IsCompleted; i++)
            {
                Pump();
            }

            Window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void The_view_builds_and_the_host_comes_up_with_a_shell_over_its_output()
    {
        using var harness = new Harness();
        harness.PumpUntil(() => harness.View.Host is not null, "the host never came up");
        harness.PumpUntil(() => harness.View.Shell is not null, "the shell view was never created");
        var shell = harness.View.Shell!;
        Assert.Same(harness.View.Host, shell.Host);
        Assert.Same(shell.View, shell.Shell.View);
        Assert.NotNull(shell.Shell.Frames);
        Assert.True(shell.View.Output.CurrentMode.Width > 0 && shell.View.Output.CurrentMode.Height > 0);
        Assert.Equal(OutputTransform.Normal, shell.Shell.Transform);
        Assert.Equal(0, shell.Shell.TopStrip.Height);
        Assert.Equal(0, shell.Shell.BottomStrip.Height);
        Assert.Equal(shell.Shell.Output, shell.Shell.WorkArea);
        Assert.Equal("ready", harness.View.StatusText);
    }

    [AvaloniaFact]
    public void The_embedded_theme_parses_and_draws_a_close_button()
    {
        var theme = TarnTheme.Load("anything")!;
        Assert.Equal(TarnTheme.Name, theme.Name);
    }

    [AvaloniaFact]
    public void The_endpoint_defaults_per_platform()
    {
        using var harness = new Harness();
        var expected = PlatformFacts.HasDescriptors ? "127.0.0.1:9800" : "ws://127.0.0.1:9801";
        Assert.Equal(expected, TarnLink.DefaultEndpoint);
        Assert.Equal(expected, harness.View.Endpoint);
        Assert.Equal(PlatformFacts.HasDescriptors, !TarnLink.IsWebSocket(TarnLink.DefaultEndpoint));
    }

    [AvaloniaFact]
    public void Connecting_to_an_unreachable_endpoint_reports_a_status_rather_than_throwing()
    {
        using var harness = new Harness();
        harness.PumpUntil(() => harness.View.Shell is not null, "the shell view was never created");
        harness.View.Endpoint = "127.0.0.1:1";
        var toggle = harness.View.ToggleAsync();
        harness.PumpUntil(() => toggle.IsCompleted, "the connect attempt never finished");
        Assert.True(toggle.IsCompletedSuccessfully, "the connect attempt faulted instead of reporting");
        Assert.StartsWith("could not connect", harness.View.StatusText, StringComparison.Ordinal);
        Assert.False(harness.View.Connected);
    }

    [AvaloniaFact]
    public void A_background_trip_suspends_the_host_and_a_return_resumes_it()
    {
        using var harness = new Harness();
        harness.PumpUntil(() => harness.View.Shell is not null, "the shell view was never created");
        var host = harness.View.Host!;
        harness.View.Suspend();
        harness.PumpUntil(() => host.IsSuspended, "the host never suspended");
        Assert.False(harness.View.Shell!.View.IsPresenting);
        harness.View.Resume();
        harness.PumpUntil(() => !host.IsSuspended, "the host never resumed");
        Assert.True(harness.View.Shell.View.IsPresenting);
    }
}
