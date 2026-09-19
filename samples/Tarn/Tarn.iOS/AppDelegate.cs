using Avalonia;
using Avalonia.iOS;
using Foundation;

namespace Tarn.iOS;

[Register("AppDelegate")]
public sealed class AppDelegate : AvaloniaAppDelegate<TarnApp>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder)
            .With(new iOSPlatformOptions
            {
                RenderingMode = [iOSRenderingMode.Metal, iOSRenderingMode.OpenGl],
            });
}
