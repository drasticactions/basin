using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TabWm.Chrome;

public sealed partial class GhostView : UserControl
{
    public GhostView() => AvaloniaXamlLoader.Load(this);
}
