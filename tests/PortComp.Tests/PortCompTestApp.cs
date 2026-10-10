using Avalonia;
using Avalonia.Headless;

namespace PortComp.Tests;

public sealed class PortCompTestApp : Application
{
    public override void Initialize() => Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<PortCompTestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
