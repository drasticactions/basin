using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Avalonia.Markup.Xaml;

namespace PlasmaHost.Shell;

public sealed partial class BreezeButtonView : UserControl
{
    public BreezeButtonView()
    {
        AvaloniaXamlLoader.Load(this);
        var duration = BreezeAnimations.Duration;
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        if (this.FindControl<Ellipse>("Halo") is { } halo)
        {
            halo.Transitions =
            [
                new DoubleTransition { Property = OpacityProperty, Duration = duration },
                new BrushTransition { Property = Shape.FillProperty, Duration = duration },
            ];
        }

        if (this.FindControl<ShapePath>("Glyph") is { } glyph)
        {
            glyph.Transitions =
            [
                new BrushTransition { Property = Shape.StrokeProperty, Duration = duration },
            ];
        }
    }
}
