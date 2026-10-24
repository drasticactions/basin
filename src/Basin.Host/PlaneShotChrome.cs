using Basin.Scene;

namespace Basin.Host;

/// <summary>
/// Names a chrome buffer that <see cref="PlaneShot.Write"/> writes as its own image.
/// </summary>
/// <param name="Name">The name in the file name and in the report line.</param>
/// <param name="Buffer">The buffer to write, or <see langword="null"/> to use the buffer of <paramref name="Node"/>.</param>
/// <param name="Node">The scene node that shows the buffer, or <see langword="null"/>. A named node is not written again as a backdrop.</param>
/// <param name="Detail">Extra text for the report line, or <see langword="null"/>.</param>
public readonly record struct PlaneShotChrome(string Name, IBuffer? Buffer, SceneBuffer? Node = null, string? Detail = null);
