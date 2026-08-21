using Avalonia;
using Avalonia.Headless;
using Westonia.Shell;

namespace Westonia.Tests;

public static class WestoniaTestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<ShellApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
