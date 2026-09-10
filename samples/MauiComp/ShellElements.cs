using Basin.Scene;
using MauiComp.Shell;

namespace MauiComp;

internal sealed class ShellElements : IDisposable
{
    public required OutputUISurface BackgroundSurface { get; init; }

    public required OutputUISurface PanelSurface { get; init; }

    public BackgroundModel Background { get; } = new();

    public PanelModel Panel { get; } = new();

    public MauiScope? BackgroundScope { get; set; }

    public MauiScope? PanelScope { get; set; }

    public void Dispose()
    {
        BackgroundScope?.Dispose();
        BackgroundScope = null;
        PanelScope?.Dispose();
        PanelScope = null;
        BackgroundSurface.Dispose();
        PanelSurface.Dispose();
    }
}
