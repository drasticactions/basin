namespace Basin.Scene;

/// <summary>
/// A mesh deformation that can map a point back into the undeformed content, so that hit testing reaches the content under the mesh.
/// </summary>
public interface IInvertibleMeshTransform : IMeshTransform
{
    /// <summary>
    /// Maps a point on the deformed result back to the undeformed content.
    /// </summary>
    /// <param name="childBounds">The undeformed content bounds, in the transform's local coordinates.</param>
    /// <param name="x">The horizontal coordinate on the deformed result.</param>
    /// <param name="y">The vertical coordinate on the deformed result.</param>
    /// <param name="sourceX">The horizontal coordinate in the undeformed content.</param>
    /// <param name="sourceY">The vertical coordinate in the undeformed content.</param>
    /// <returns><see langword="false"/> when the point is outside the mesh. Hit testing then skips the subtree.</returns>
    bool TryMapToSource(in Box childBounds, double x, double y, out double sourceX, out double sourceY);
}
