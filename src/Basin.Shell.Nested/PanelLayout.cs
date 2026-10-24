namespace Basin.Shell.Nested;

/// <summary>
/// The geometry of the panel strips that the shell reserves out of the work area.
/// A strip with zero items takes no space. The consumer fills the strips.
/// </summary>
/// <param name="Size">The height of each strip in logical pixels.</param>
/// <param name="Top">The number of items in the top strip. Zero removes the top strip.</param>
/// <param name="Bottom">The number of items in the bottom strip. Zero removes the bottom strip.</param>
public sealed record PanelLayout(int Size, int Top, int Bottom);
