using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PlasmaHost.Shell;

public sealed partial class DesktopOsdView : UserControl
{
    public DesktopOsdView() => AvaloniaXamlLoader.Load(this);
}
