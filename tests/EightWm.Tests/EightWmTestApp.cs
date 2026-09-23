using Avalonia;
using Avalonia.Headless;
using AvaWin;
using AvaWin.Animations;

namespace EightWm.Tests;

public static class EightWmTestApp
{
    public static AppBuilder BuildAvaloniaApp()
    {
        WinAnimations.TimeScale = 0;
        return AppBuilder.Configure<EightWmApp>()
            .UseSkia()
            .UseHarfBuzz()
            .WithAvaWinFonts()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
