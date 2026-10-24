using SkiaSharp;

namespace Basin.Sdl;

/// <summary>
/// Draws frames into one SDL window through a Skia canvas.
/// A frame is one <see cref="Begin"/> followed by one <see cref="Present"/> or one <see cref="Discard"/>, on the SDL main thread.
/// </summary>
public interface ISdlRenderLease : IDisposable
{
    /// <summary>
    /// Gets the renderer name of the device that created the lease.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Starts a frame. The canvas is in device pixels and stays valid until <see cref="Present"/> or <see cref="Discard"/>.
    /// </summary>
    /// <param name="canvas">The canvas to draw the frame on. It is not valid when the method returns <see langword="false"/>.</param>
    /// <param name="context">The Skia context of the canvas, or <see langword="null"/> for a raster lease.</param>
    /// <param name="age">The number of frames since the target last held a presented frame, or 0 when its contents are unknown.</param>
    /// <returns><see langword="false"/> when no frame can start now, for example after a context loss.</returns>
    bool Begin(out SKCanvas canvas, out GRContext? context, out int age);

    /// <summary>
    /// Flushes the frame that <see cref="Begin"/> started and shows it in the window.
    /// </summary>
    void Present();

    /// <summary>
    /// Ends the frame that <see cref="Begin"/> started without showing it. The window keeps the frame it showed last.
    /// The default does nothing, for a lease that holds nothing between <see cref="Begin"/> and <see cref="Present"/>.
    /// </summary>
    void Discard()
    {
    }

    /// <summary>
    /// Sets the size of the drawing target. A value below 1 counts as 1.
    /// </summary>
    /// <param name="pixelWidth">The width in device pixels.</param>
    /// <param name="pixelHeight">The height in device pixels.</param>
    void Resize(int pixelWidth, int pixelHeight);

    /// <summary>
    /// Copies the current target into memory, before <see cref="Present"/>.
    /// </summary>
    /// <param name="info">The size and format to read.</param>
    /// <param name="pixels">The memory that receives the pixels.</param>
    /// <param name="rowBytes">The stride of <paramref name="pixels"/> in bytes.</param>
    /// <returns><see langword="false"/> when the lease cannot read its target back. The caller must then render the frame offscreen.</returns>
    bool ReadPixels(SKImageInfo info, nint pixels, int rowBytes);

    /// <summary>
    /// Makes the context of the lease current on the calling thread, for GL work outside a frame.
    /// </summary>
    /// <returns><see langword="false"/> when the context is missing or cannot be made current.</returns>
    bool MakeCurrent();

    /// <summary>
    /// Gets the HDR state that the next frame presents. It is <see cref="SdlHdrState.Sdr"/> for a lease that cannot present HDR.
    /// </summary>
    SdlHdrState Hdr { get; }

    /// <summary>
    /// Asks the lease to present in HDR or SDR. Read <see cref="Hdr"/> after the call for the state that the next frame presents.
    /// </summary>
    /// <param name="state">The requested state.</param>
    /// <returns><see langword="false"/> when the lease cannot present <paramref name="state"/>.</returns>
    bool TrySetHdr(in SdlHdrState state) => false;
}
