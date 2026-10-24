namespace Basin.Capabilities;

/// <summary>
/// How a backend builds the timing of a custom mode.
/// </summary>
public enum OutputModeGenerator
{
    /// <summary>
    /// <see cref="SameTiming"/> on a built-in panel and <see cref="Cvt"/> on other outputs.
    /// </summary>
    Auto = 0,
    /// <summary>
    /// A VESA CVT timing with reduced blanking.
    /// </summary>
    Cvt = 1,
    /// <summary>
    /// The timing of an existing mode, with the vertical blanking changed to reach the refresh rate.
    /// </summary>
    SameTiming = 2,
    /// <summary>
    /// The timing of an existing mode, with the pixel clock scaled to reach the refresh rate.
    /// </summary>
    ScaledClock = 3,
}
