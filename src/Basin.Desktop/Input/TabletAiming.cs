using Basin.Capabilities;

namespace Basin.Desktop;

/// <summary>
/// Gives tablet tool focus to the surface under a tool position, for a tablet that maps onto one output.
/// </summary>
public static class TabletAiming
{
    /// <summary>
    /// Maps normalized tool axes onto <paramref name="output"/>, finds the surface at that point, and gives it the tool focus. It clears the focus when no surface is hit.
    /// </summary>
    /// <param name="tool">The tool to move.</param>
    /// <param name="scene">The scene to hit-test.</param>
    /// <param name="layout">The layout that places <paramref name="output"/>.</param>
    /// <param name="output">The output that the tablet area maps onto.</param>
    /// <param name="axes">The tool axes. X and Y are in the range 0 to 1 across the output.</param>
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
