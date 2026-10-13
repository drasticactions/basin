using Avalonia;
using Avalonia.Headless;
using BasinGreeter.UI;

namespace BasinGreeter.Tests;

public static class GreeterTestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<GreeterApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
