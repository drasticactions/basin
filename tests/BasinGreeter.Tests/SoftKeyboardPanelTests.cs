using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Basin.SoftKeyboard.Avalonia;
using Xunit;

namespace BasinGreeter.Tests;

public sealed class SoftKeyboardPanelTests
{
    private static (Window Window, SoftKeyboardPanel Panel, Border Keys, Border Above) Show()
    {
        var keys = new Border { Height = 200 };
        var panel = new SoftKeyboardPanel
        {
            OpenDuration = TimeSpan.FromMilliseconds(300),
            CloseDuration = TimeSpan.FromMilliseconds(300),
            Easing = new LinearEasing(),
            Child = keys,
        };
        DockPanel.SetDock(panel, Dock.Bottom);
        var above = new Border();
        var window = new Window
        {
            Width = 400,
            Height = 600,
            SizeToContent = SizeToContent.Manual,
            Content = new DockPanel { LastChildFill = true, Children = { panel, above } },
        };
        window.Show();
        Pump();
        return (window, panel, keys, above);
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static void WaitWhile(Window window, Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (condition() && Environment.TickCount64 < deadline)
        {
            var frame = new DispatcherFrame();
            DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(10));
            Dispatcher.UIThread.PushFrame(frame);
            window.UpdateLayout();
        }
    }

    [AvaloniaFact]
    public void Opening_slides_the_keyboard_up_and_the_content_above_follows_it()
    {
        var (window, panel, keys, above) = Show();
        Assert.Equal(0, panel.Revealed);
        Assert.False(keys.IsVisible);
        Assert.Equal(600, above.Bounds.Height);

        panel.IsOpen = true;
        Assert.True(panel.IsSliding);
        WaitWhile(window, () => panel.Revealed < 20);
        window.UpdateLayout();
        Assert.True(panel.IsSliding, "the slide finished before it could be seen moving");
        Assert.InRange(panel.Revealed, 1, 199);
        Assert.True(keys.IsVisible);
        Assert.Equal(600 - panel.Revealed, above.Bounds.Height);
        Assert.Equal(panel.Revealed, Math.Round(panel.Revealed));

        WaitWhile(window, () => panel.IsSliding);
        window.UpdateLayout();
        Assert.Equal(200, panel.Revealed);
        Assert.Equal(200, panel.Bounds.Height);
        Assert.Equal(0, keys.Bounds.Y);
        Assert.Equal(400, above.Bounds.Height);
        window.Close();
    }

    [AvaloniaFact]
    public void Closing_slides_it_away_and_hides_it_and_settle_jumps_to_the_end()
    {
        var (window, panel, keys, above) = Show();
        panel.IsOpen = true;
        panel.Settle();
        window.UpdateLayout();
        Assert.False(panel.IsSliding);
        Assert.Equal(200, panel.Revealed);

        panel.IsOpen = false;
        Assert.True(panel.IsSliding);
        WaitWhile(window, () => panel.IsSliding);
        window.UpdateLayout();
        Assert.Equal(0, panel.Revealed);
        Assert.False(keys.IsVisible);
        Assert.Equal(600, above.Bounds.Height);
        window.Close();
    }
}
