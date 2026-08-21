using Avalonia;
using Avalonia.Markup.Xaml;

namespace Westonia.Shell;

public sealed class ShellApp : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}
