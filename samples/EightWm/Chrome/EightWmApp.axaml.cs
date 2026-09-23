using Avalonia;
using Avalonia.Markup.Xaml;

namespace EightWm;

public sealed class EightWmApp : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}
