using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BasinGreeter.UI;

public sealed partial class BackgroundView : UserControl
{
    public BackgroundView() => AvaloniaXamlLoader.Load(this);
}
