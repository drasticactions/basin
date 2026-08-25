using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PlasmaHost.Shell;

public sealed partial class TilesEditorView : UserControl
{
    public TilesEditorView() => AvaloniaXamlLoader.Load(this);
}
