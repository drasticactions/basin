using Basin.Scene;
using Basin.UI.Avalonia;

namespace PlasmaHost;

internal sealed class PlasmaHostFrames : IDisposable
{
    private readonly AvaloniaUIHost _uiHost;
    private readonly UISurfaceIndex _index;
    private readonly BreezeIcons _icons = new();

    public PlasmaHostFrames(AvaloniaUIHost uiHost, UISurfaceIndex index, BreezeTheme theme)
    {
        _uiHost = uiHost;
        _index = index;
        Theme = theme;
        Shadows = new PlasmaHostShadows(BreezeMetrics.CornerRadius);
    }

    public BreezeTheme Theme { get; }

    public PlasmaHostShadows Shadows { get; }

    public PlasmaFrame Create(SceneTree parent) =>
        new(_uiHost, Theme, _icons, parent, _index) { TouchSlop = 12 };

    public void Dispose()
    {
        Shadows.Dispose();
        _icons.Dispose();
    }
}
