using Avalonia;
using Avalonia.Markup.Xaml;
using AvaUnion;

namespace TabWm.Chrome;

public sealed partial class TabWmApp : Application
{
    public UnionTheme Union => (UnionTheme)Styles[0];

    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}
