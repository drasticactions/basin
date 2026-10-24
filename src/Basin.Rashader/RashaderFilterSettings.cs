namespace Basin.Rashader;

/// <summary>
/// The settings that a GL or Vulkan librashader filter reads once at creation.
/// </summary>
public readonly record struct RashaderFilterSettings
{
    /// <summary>
    /// Gets a value indicating whether the filter asks for a repaint on every frame. Set it for an
    /// animated preset. The consumer pays the cost.
    /// </summary>
    public bool ContinuousRepaint { get; init; }

    /// <summary>
    /// Gets a value indicating whether librashader skips its on-disk shader cache. Each creation then
    /// compiles every pass again.
    /// </summary>
    public bool DisableCache { get; init; }

    /// <summary>
    /// Gets a value indicating whether the preset is set up for a vertical screen orientation. The
    /// default is horizontal.
    /// </summary>
    public bool VerticalScreen { get; init; }
}
