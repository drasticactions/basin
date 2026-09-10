using Avalonia;
using Avalonia.Headless;

namespace MauiComp.Tests;

public static class MauiCompTestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<ShellAvaloniaApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
