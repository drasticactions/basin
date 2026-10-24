namespace Basin.Rashader;

/// <summary>
/// One runtime parameter that a librashader preset declares.
/// </summary>
/// <param name="Name">The name that <see cref="IRashaderFilter.TrySetParameter(string, float)"/> takes.</param>
/// <param name="Description">The human-readable label from the preset, or an empty string.</param>
/// <param name="Initial">The value the preset starts with.</param>
/// <param name="Minimum">The lowest value the preset declares.</param>
/// <param name="Maximum">The highest value the preset declares.</param>
/// <param name="Step">The increment the preset suggests for a control.</param>
public readonly record struct RashaderParameter(
    string Name,
    string Description,
    float Initial,
    float Minimum,
    float Maximum,
    float Step);
