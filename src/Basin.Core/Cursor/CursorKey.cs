using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin;

/// <summary>
/// Selects one variant of a cursor theme: a size for an output scale, and a color conversion for an output.
/// </summary>
/// <param name="Scale">The output scale. <see cref="CursorImages"/> snaps it before the lookup.</param>
/// <param name="Output">The image description of the output, or <see langword="null"/> for no color conversion.</param>
public readonly record struct CursorKey(double Scale, ImageDescription? Output);
