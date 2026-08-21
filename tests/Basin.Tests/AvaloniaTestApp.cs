using Avalonia;
using Avalonia.Headless;

namespace Basin.Tests;

public sealed class AvaloniaTestApp : Application
{
    public override void Initialize() => Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<AvaloniaTestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
