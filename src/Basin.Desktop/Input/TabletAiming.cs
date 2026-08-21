using Basin.Capabilities;

namespace Basin.Desktop;

public static class TabletAiming
{
    public static void AimAt(
        this TabletManager.TabletTool tool, Scene.Scene scene, OutputLayout layout, IOutput output,
        in TabletToolAxes axes)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(output);
        var bounds = layout.BoxOf(output);
        var hit = scene.SurfaceAt(bounds.X + (axes.X * bounds.Width), bounds.Y + (axes.Y * bounds.Height));
        tool.SetFocus(hit?.Surface, hit?.X ?? 0, hit?.Y ?? 0);
    }
}
