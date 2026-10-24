namespace Basin.Rashader;

/// <summary>
/// The options that <see cref="RashaderPreset.TryCreate"/> applies to the preset context before it
/// parses the preset.
/// </summary>
public readonly record struct RashaderPresetOptions
{
    /// <summary>
    /// Gets the shader runtime that the preset is preprocessed for.
    /// </summary>
    public RashaderRuntime Runtime { get; init; }

    /// <summary>
    /// Gets a value indicating whether the screen and view aspect orientations are vertical instead of
    /// horizontal.
    /// </summary>
    public bool VerticalScreen { get; init; }
}
