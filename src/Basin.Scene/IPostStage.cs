namespace Basin.Scene;

/// <summary>
/// A full-frame effect that a <see cref="SceneOutput"/> applies after it composites the scene, for example a shader filter.
/// </summary>
public interface IPostStage
{
    /// <summary>
    /// Draws the composited frame into the output pass with the effect applied.
    /// </summary>
    /// <param name="pass">The pass on the output buffer.</param>
    /// <param name="frame">The texture that holds the composited scene.</param>
    /// <param name="context">The frame size, timing and damage.</param>
    void Render(IRenderPass pass, ITexture frame, in PostContext context);

    /// <summary>
    /// Maps a box in the composited frame to where the effect draws it on the output. The default implementation declines.
    /// </summary>
    /// <param name="scene">The box in the composited frame, in physical pixels.</param>
    /// <param name="context">The frame size, timing and damage.</param>
    /// <param name="output">The mapped box, or <paramref name="scene"/> when the stage cannot map it.</param>
    /// <returns><see langword="true"/> when the stage knows where the box lands on the output, also when it lands in the same place. <see langword="false"/> when the stage cannot map it.</returns>
    bool TryMapBox(in Box scene, in PostContext context, out Box output)
    {
        output = scene;
        return false;
    }
}
