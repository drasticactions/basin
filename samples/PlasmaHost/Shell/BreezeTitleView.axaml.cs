using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;

namespace PlasmaHost.Shell;

public sealed partial class BreezeTitleView : UserControl
{
    public BreezeTitleView()
    {
        AvaloniaXamlLoader.Load(this);
        var duration = BreezeAnimations.Duration;
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        if (this.FindControl<Border>("Chrome") is { } chrome)
        {
            chrome.Transitions =
            [
                new BrushTransition { Property = Border.BackgroundProperty, Duration = duration },
            ];
        }

        if (this.FindControl<TextBlock>("TitleText") is { } text)
        {
            text.Transitions =
            [
                new BrushTransition { Property = TextBlock.ForegroundProperty, Duration = duration },
            ];
        }
    }
}
