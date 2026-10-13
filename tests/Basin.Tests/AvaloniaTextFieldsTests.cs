using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Basin.Capabilities;
using Basin.UI.Avalonia;
using Xunit;

namespace Basin.Tests;

public sealed class AvaloniaTextFieldsTests
{
    private sealed class OneScreen : IUIScreenSource
    {
        public int Count => 1;

        public bool TryGet(int index, out UIScreenInfo info)
        {
            info = new UIScreenInfo("shell", 0, 0, 800, 600, 1.0, true);
            return index == 0;
        }

        public event Action? Changed
        {
            add
            {
            }

            remove
            {
            }
        }
    }

    private sealed class Pane : Border, IInputPaneView
    {
    }

    private static void Pump(int rounds = 5)
    {
        for (var i = 0; i < rounds; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void A_focused_password_box_is_reported_and_each_touch_is_placed_on_it_the_pane_or_elsewhere()
    {
        var owner = Basin.Diagnostics.ThreadAffinity.Capture();
        using var host = BasinPlatform.Attach(new BasinPlatformOptions { Screens = new OneScreen(), CompositorAffinity = owner });
        var surface = (AvaloniaUISurface)host.CreateSurface(new UISurfaceOptions
        {
            Target = UITargetKind.Memory,
            Width = 400,
            Height = 300,
            Scale = 1.0,
        })!;
        var box = new SecretTextBox { PasswordChar = '*', Width = 200, Height = 40, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var other = new Button { Content = "Other", Width = 100, Height = 40, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        var pane = new Pane { Height = 100, VerticalAlignment = VerticalAlignment.Bottom, Background = global::Avalonia.Media.Brushes.Gray };
        surface.Content = new Grid { Background = global::Avalonia.Media.Brushes.Black, Children = { box, other, pane } };
        Pump();

        var fields = host.TextFields;
        var changes = 0;
        fields.FocusChanged += () => changes++;
        var touches = new List<UITouchTarget>();
        fields.Touched += touches.Add;
        Assert.Null(fields.Focused);

        box.Focus();
        Pump();
        Assert.Equal(1, changes);
        var focused = Assert.NotNull(fields.Focused);
        Assert.Equal(UITextField.PurposePassword, focused.Purpose);
        Assert.NotEqual(0u, focused.Hint & UITextField.HintHiddenText);

        surface.NotifyTouchDown(1, 1, 100, 20);
        surface.NotifyTouchUp(2, 1);
        surface.NotifyTouchDown(3, 2, 200, 250);
        surface.NotifyTouchUp(4, 2);
        surface.NotifyTouchDown(5, 3, 200, 150);
        surface.NotifyTouchUp(6, 3);
        Pump();
        Assert.Equal([UITouchTarget.Field, UITouchTarget.InputPane, UITouchTarget.Elsewhere], touches);
        Assert.NotNull(fields.Focused);

        other.Focus();
        Pump();
        Assert.Null(fields.Focused);
        Assert.Equal(2, changes);
        surface.Dispose();
    }
}
