using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TabWm.Chrome;

public sealed partial class SettingsView : UserControl
{
    public SettingsView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
