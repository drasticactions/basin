namespace Basin.Capabilities;

/// <summary>
/// One screen that an <see cref="IUIScreenSource"/> reports.
/// </summary>
/// <param name="Name">The output name.</param>
/// <param name="X">The left edge in layout coordinates.</param>
/// <param name="Y">The top edge in layout coordinates.</param>
/// <param name="Width">The width in logical pixels.</param>
/// <param name="Height">The height in logical pixels.</param>
/// <param name="Scale">The output scale.</param>
/// <param name="IsPrimary">Whether this is the primary screen, which is the first one.</param>
public readonly record struct UIScreenInfo(
    string Name,
    int X,
    int Y,
    int Width,
    int Height,
    double Scale,
    bool IsPrimary);
