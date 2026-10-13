using Avalonia;
using Avalonia.Markup.Xaml;

namespace BasinGreeter.UI;

public sealed class GreeterApp : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}
