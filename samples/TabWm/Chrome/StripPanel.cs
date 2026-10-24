using Avalonia;
using Avalonia.Controls;

namespace TabWm.Chrome;

public sealed class StripPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count < 3)
        {
            return default;
        }

        var infinite = new Size(double.PositiveInfinity, availableSize.Height);
        Children[1].Measure(infinite);
        Children[2].Measure(infinite);
        var fixedWidth = Children[1].DesiredSize.Width + Children[2].DesiredSize.Width;
        var tabsWidth = Math.Max(0, availableSize.Width - fixedWidth);
        Children[0].Measure(new Size(tabsWidth, availableSize.Height));
        var height = Math.Max(Children[0].DesiredSize.Height, Math.Max(Children[1].DesiredSize.Height, Children[2].DesiredSize.Height));
        var width = double.IsInfinity(availableSize.Width) ? Children[0].DesiredSize.Width + fixedWidth : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count < 3)
        {
            return finalSize;
        }

        var plus = Children[1].DesiredSize.Width;
        var status = Children[2].DesiredSize.Width;
        var room = Math.Max(0, finalSize.Width - plus - status);
        var tabs = Math.Min(Children[0].DesiredSize.Width, room);
        Children[0].Arrange(new Rect(0, 0, tabs, finalSize.Height));
        Children[1].Arrange(new Rect(tabs, 0, plus, finalSize.Height));
        Children[2].Arrange(new Rect(finalSize.Width - status, 0, status, finalSize.Height));
        return finalSize;
    }
}
