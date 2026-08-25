using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.UI.Avalonia;
using Westonia.Shell;

namespace Westonia;

internal sealed class ShellElements : IDisposable
{
    public required OutputUISurface BackgroundSurface { get; init; }

    public required OutputUISurface PanelSurface { get; init; }

    public PanelModel Panel { get; } = new();

    public BackgroundModel Background { get; } = new();

    public void Dispose()
    {
        BackgroundSurface.Dispose();
        PanelSurface.Dispose();
    }
}
