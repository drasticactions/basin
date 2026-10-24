namespace Basin.Sdl;

/// <summary>
/// The HDR state of an SDL window or lease, with luminance in nits.
/// </summary>
/// <param name="Enabled">A value indicating whether the window presents HDR content.</param>
/// <param name="SdrWhiteNits">The luminance of SDR white in nits.</param>
/// <param name="Headroom">The ratio of the peak luminance to SDR white. It is 1 for SDR.</param>
public readonly record struct SdlHdrState(bool Enabled, float SdrWhiteNits, float Headroom)
{
    /// <summary>
    /// Gets the SDR state: HDR off, SDR white at the 203-nit BT.2408 reference, and no headroom.
    /// </summary>
    public static SdlHdrState Sdr => new(false, 203f, 1f);

    /// <summary>
    /// Gets the peak luminance in nits, which is SDR white times the headroom.
    /// </summary>
    public float PeakNits => SdrWhiteNits * Headroom;
}
