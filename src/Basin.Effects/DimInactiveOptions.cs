namespace Basin.Effects;

/// <summary>
/// The settings of a <see cref="DimInactiveEffect{TWindow}"/>.
/// </summary>
public readonly record struct DimInactiveOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DimInactiveOptions"/> struct with the default values.
    /// </summary>
    public DimInactiveOptions()
    {
    }

    /// <summary>
    /// Gets the dim strength in percent. The effect clamps it to the range from 10 to 90.
    /// </summary>
    public int Strength { get; init; } = 25;

    /// <summary>
    /// Gets a value indicating whether an inactive <see cref="DimInactiveWindowKind.Panel"/> dims.
    /// </summary>
    public bool DimPanels { get; init; } = false;

    /// <summary>
    /// Gets a value indicating whether an inactive <see cref="DimInactiveWindowKind.Desktop"/> dims.
    /// </summary>
    public bool DimDesktop { get; init; } = false;

    /// <summary>
    /// Gets a value indicating whether an inactive window that stays above other windows dims.
    /// </summary>
    public bool DimKeepAbove { get; init; } = false;

    /// <summary>
    /// Gets a value indicating whether the windows in the group of the active window stay undimmed with it.
    /// </summary>
    public bool DimByGroup { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether an inactive full-screen window dims.
    /// </summary>
    public bool DimFullScreen { get; init; } = true;

    /// <summary>
    /// Gets the duration in milliseconds of the fade to full brightness when a window becomes active.
    /// </summary>
    public double ActivateMillis { get; init; } = 160;

    /// <summary>
    /// Gets the duration in milliseconds of the fade to the dim when a window stops being active.
    /// </summary>
    public double DeactivateMillis { get; init; } = 250;

    /// <summary>
    /// Gets the duration in milliseconds of the fade that removes or restores every dim when a full-screen effect starts or ends.
    /// </summary>
    public double FullScreenMillis { get; init; } = 250;
}
