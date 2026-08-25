using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PlasmaHost.Shell;

public sealed partial class BreezeEdgeView : UserControl
{
    public BreezeEdgeView()
    {
        AvaloniaXamlLoader.Load(this);
        var duration = BreezeAnimations.Duration;
        if (duration > TimeSpan.Zero && this.FindControl<Border>("Chrome") is { } chrome)
        {
            chrome.Transitions =
            [
                new BrushTransition { Property = Border.BackgroundProperty, Duration = duration },
            ];
        }
    }
}
