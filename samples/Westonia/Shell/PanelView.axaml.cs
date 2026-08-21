using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Westonia.Shell;

public sealed partial class PanelView : UserControl
{
    public PanelView() => AvaloniaXamlLoader.Load(this);
}
