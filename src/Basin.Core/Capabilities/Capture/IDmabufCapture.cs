namespace Basin.Capabilities;

/// <summary>
/// Exports the last composited frame of an output as a dmabuf, without a copy.
/// </summary>
public interface IDmabufCapture
{
    /// <summary>
    /// Gets the dmabuf of the frame that an output last composited.
    /// </summary>
    /// <param name="output">The output to read.</param>
    /// <param name="attributes">The dmabuf planes and format. The file descriptors stay owned by the implementation.</param>
    /// <returns><see langword="true"/> when a dmabuf frame exists and no excluded content is visible, otherwise <see langword="false"/>.</returns>
    bool TryCurrentFrame(IOutput output, out DmabufAttributes attributes);
}
