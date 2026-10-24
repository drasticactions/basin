using Basin.Shell.Xdg;

namespace Basin.Avalonia;

/// <summary>
/// Maps layer-shell layers to host stacking bands.
/// </summary>
public static class HostStacking
{
    /// <summary>
    /// Gets the band for a layer.
    /// </summary>
    /// <param name="layer">The layer of the layer surface.</param>
    /// <returns>The band. A layer with no match gets <see cref="HostStackingBand.Above"/>.</returns>
    public static HostStackingBand BandFor(LayerKind layer) => layer switch
    {
        LayerKind.Background => HostStackingBand.Background,
        LayerKind.Bottom => HostStackingBand.Below,
        LayerKind.Overlay => HostStackingBand.Overlay,
        _ => HostStackingBand.Above,
    };

    /// <summary>
    /// Finds whether a band needs a topmost host window.
    /// </summary>
    /// <param name="band">The band.</param>
    /// <returns><see langword="true"/> for <see cref="HostStackingBand.Above"/> and <see cref="HostStackingBand.Overlay"/>.</returns>
    public static bool IsTopmost(HostStackingBand band) =>
        band is HostStackingBand.Above or HostStackingBand.Overlay;

    internal static void Apply(global::Avalonia.Controls.Window window, HostStackingBand band, bool takesKeyboard)
    {
        if (window.TryGetPlatformHandle() is not { } handle)
        {
            return;
        }

        try
        {
            if (OperatingSystem.IsMacOS())
            {
                MacWindowLevel.Apply(handle, band);
            }
            else if (OperatingSystem.IsWindows())
            {
                Win32WindowLevel.Apply(handle, band, takesKeyboard);
            }
            else if (OperatingSystem.IsLinux())
            {
                X11WindowLevel.Apply(handle, band);
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            AvaloniaLog.Log.Debug(error, $"stacking: window level not applied, platform library missing");
        }
    }
}
