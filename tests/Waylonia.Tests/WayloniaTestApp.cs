using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

namespace Waylonia.Tests;

public sealed class WayloniaTestApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<WayloniaTestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
