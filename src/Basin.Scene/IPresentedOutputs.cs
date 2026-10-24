using System.Diagnostics.CodeAnalysis;

namespace Basin.Scene;

/// <summary>
/// Gives diagnostics access to the outputs that a driver presents and to the buffer each one last put on screen.
/// </summary>
public interface IPresentedOutputs
{
    /// <summary>
    /// Gets the scene outputs that the driver presents now. An implementation can build a new list on each call.
    /// </summary>
    IReadOnlyList<SceneOutput> Outputs { get; }

    /// <summary>
    /// Occurs after an output presents a frame.
    /// </summary>
    event Action<SceneOutput>? Presented;

    /// <summary>
    /// Gets or sets a value indicating whether the driver must keep the last presented buffer readable.
    /// </summary>
    bool CapturePresented { get; set; }

    /// <summary>
    /// Reads back the buffer that an output last presented.
    /// </summary>
    /// <param name="output">The output to read.</param>
    /// <param name="renderer">The renderer that can read the buffer.</param>
    /// <param name="rgba">The pixels as RGBA bytes, or <see langword="null"/> on failure.</param>
    /// <param name="width">The buffer width, in physical pixels.</param>
    /// <param name="height">The buffer height, in physical pixels.</param>
    /// <returns><see langword="false"/> when the output has presented nothing or the buffer cannot be read.</returns>
    bool TryReadPresented(SceneOutput output, IRenderer renderer, [NotNullWhen(true)] out byte[]? rgba, out int width, out int height);

    /// <summary>
    /// Turns plane offload and direct scanout on or off for every output.
    /// </summary>
    /// <param name="allowed"><see langword="false"/> to composite every frame.</param>
    void AllowPlanes(bool allowed);

    /// <summary>
    /// Damages the whole of an output and schedules a repaint.
    /// </summary>
    /// <param name="output">The output to repaint.</param>
    void RequestRepaint(SceneOutput output);
}
