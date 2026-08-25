using Avalonia;
using Avalonia.Markup.Xaml;

namespace PlasmaHost.Shell;

public sealed class PlasmaApp : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}
