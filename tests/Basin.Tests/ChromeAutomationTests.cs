using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Basin.UI.Avalonia;
using Xunit;

namespace Basin.Tests;

public sealed class ChromeAutomationTests
{
    private static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void A_chrome_button_takes_focus_and_enter_or_space_raises_invoked()
    {
        var invoked = 0;
        var tile = new Border { Width = 40, Height = 40 };
        Assert.False(tile.Focusable);
        ChromeAutomation.SetIsButton(tile, true);
        ChromeAutomation.AddInvokedHandler(tile, (_, _) => invoked++);
        var window = new Window { Content = tile };
        window.Show();
        Assert.True(tile.Focusable);
        Assert.True(tile.Focus(NavigationMethod.Tab));

        Press(window, PhysicalKey.Enter);
        Press(window, PhysicalKey.Space);
        Assert.Equal(2, invoked);

        Press(window, PhysicalKey.Enter, RawInputModifiers.Control);
        Press(window, PhysicalKey.A);
        Assert.Equal(2, invoked);

        ChromeAutomation.SetIsButton(tile, false);
        Assert.False(tile.Focusable);
        Press(window, PhysicalKey.Enter);
        Assert.Equal(2, invoked);
        window.Close();
    }
}
