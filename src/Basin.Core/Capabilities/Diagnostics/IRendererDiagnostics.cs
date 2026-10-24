namespace Basin.Capabilities;

/// <summary>
/// Reads which renderer draws the outputs, how it finds a lost device, and how many times a lost renderer was replaced.
/// </summary>
public interface IRendererDiagnostics
{
    /// <summary>
    /// Gets the type name of the renderer that draws now, or <see langword="null"/> before one exists.
    /// </summary>
    string? RendererName { get; }

    /// <summary>
    /// Gets how the current renderer finds a loss of its device.
    /// </summary>
    RendererLossDetection LossDetection { get; }

    /// <summary>
    /// Gets the number of lost renderers that were replaced.
    /// </summary>
    long Rebuilds { get; }
}
