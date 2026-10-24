using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TabWm.Chrome;

public sealed partial class FrameEdgeView : UserControl
{
    public FrameEdgeView()
        : this(new Thickness(1))
    {
    }

    public FrameEdgeView(Thickness line)
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<Border>("Line")!.BorderThickness = line;
    }
}
