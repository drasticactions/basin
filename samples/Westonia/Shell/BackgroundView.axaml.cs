using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Westonia.Shell;

public sealed partial class BackgroundView : UserControl
{
    public BackgroundView() => AvaloniaXamlLoader.Load(this);
}
