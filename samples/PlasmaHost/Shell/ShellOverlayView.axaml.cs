using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PlasmaHost.Shell;

public sealed partial class ShellOverlayView : UserControl
{
    public ShellOverlayView() => AvaloniaXamlLoader.Load(this);
}
