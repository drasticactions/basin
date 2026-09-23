using Avalonia;
using Avalonia.Controls;

namespace EightWm;

public sealed partial class TitleBarView : UserControl
{
    public TitleBarView() => InitializeComponent();

    public bool IsOverClose(double x, double y) =>
        CloseButton.TranslatePoint(default, this) is { } origin &&
        new Rect(origin, CloseButton.Bounds.Size).Contains(new Point(x, y));
}
