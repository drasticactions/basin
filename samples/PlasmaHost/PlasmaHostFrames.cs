using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.UI.Skia;
using Microsoft.Extensions.Logging;

namespace PlasmaHost;

internal sealed class PlasmaHostFrames : IDisposable
{
    private readonly BreezeTheme _theme;
    private readonly IUIHost _uiHost;
    private readonly ILogger _log;

    public PlasmaHostFrames(IRenderer renderer, ILogger log)
    {
        _log = log;
        _theme = new BreezeTheme();
        _uiHost = SkiaUIHosts.For(renderer);
        Capability = new BreezeFrameRenderer(_theme);
        Shadows = new PlasmaHostShadows(BreezeFrameRenderer.CornerRadius);
    }

    public IFrameRenderer Capability { get; }

    public PlasmaHostShadows Shadows { get; }

    public SceneTree? MenuLayer { get; set; }

    public Frame Create(SceneTree parent, string label)
    {
        var frame = new Frame(_uiHost, new BreezeFrameRenderer(_theme), parent)
        {
            MenuLayer = MenuLayer,
            TouchSlop = 12,
        };
        frame.Faulted += e => _log.LogError("frame fault {Window}: {Reason}", label, e.Message);
        return frame;
    }

    public void Dispose()
    {
        Shadows.Dispose();
        _uiHost.Dispose();
        _theme.Dispose();
    }
}
