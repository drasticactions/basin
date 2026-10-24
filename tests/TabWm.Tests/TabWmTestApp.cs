using Avalonia;
using Avalonia.Headless;
using AvaUnion;
using AvaUnion.BreezeIcons;
using AvaUnion.Engine;
using TabWm.Chrome;

namespace TabWm.Tests;

public static class TabWmTestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TabWmApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .WithAvaUnion(new AvaUnionOptions { Engine = UnionEngineKind.Managed })
        .WithBreezeIcons();
}
