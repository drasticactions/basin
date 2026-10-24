namespace Basin.Effects;

/// <summary>
/// The settings of the KWin dim-inactive effect. The library reads only the strength. The other values are policy for the consumer.
/// </summary>
public readonly record struct DimInactiveOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DimInactiveOptions"/> struct with the KWin defaults.
    /// </summary>
    public DimInactiveOptions()
    {
    }

    /// <summary>
    /// Gets the dim strength in percent. <see cref="DimInactiveEffect"/> clamps it to the range from 10 to 90.
    /// </summary>
    public int Strength { get; init; } = 25;

    /// <summary>
    /// Gets a value indicating whether the consumer dims panels. The library does not read this value.
    /// </summary>
    public bool DimPanels { get; init; } = false;

    /// <summary>
    /// Gets a value indicating whether the consumer dims the desktop window. The library does not read this value.
    /// </summary>
    public bool DimDesktop { get; init; } = false;

    /// <summary>
    /// Gets a value indicating whether the consumer dims windows that stay above others. The library does not read this value.
    /// </summary>
    public bool DimKeepAbove { get; init; } = false;

    /// <summary>
    /// Gets a value indicating whether the consumer keeps the windows of the active group undimmed. The library does not read this value.
    /// </summary>
    public bool DimByGroup { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the consumer dims other windows behind a full-screen window. The library does not read this value.
    /// </summary>
    public bool DimFullScreen { get; init; } = true;

    /// <summary>
    /// Gets the fade duration in milliseconds that the consumer uses when focus changes. The library does not read this value.
    /// </summary>
    public double ActivateMillis { get; init; } = 160;

    /// <summary>
    /// Gets the fade duration in milliseconds that the consumer uses for a full-screen change. The library does not read this value.
    /// </summary>
    public double FullScreenMillis { get; init; } = 250;
}
