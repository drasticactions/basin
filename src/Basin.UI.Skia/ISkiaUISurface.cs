using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Skia;
using Pixman;
using SkiaSharp;

namespace Basin.UI.Skia;

/// <summary>
/// A UI surface that the caller draws with a Skia canvas. Each Skia UI host makes surfaces of this type, on the CPU or on a GPU.
/// </summary>
public interface ISkiaUISurface : IUISurface
{
    /// <summary>
    /// Starts a frame. The canvas is scaled so that the caller draws in logical pixels. Call <see cref="EndDraw"/> before the next draw or acquire.
    /// </summary>
    /// <returns>The canvas of the frame. It is valid only until <see cref="EndDraw"/>.</returns>
    SKCanvas BeginDraw();

    /// <summary>
    /// Ends the frame that <see cref="BeginDraw"/> started and makes it ready for <see cref="IUISurface.TryAcquire"/>.
    /// </summary>
    void EndDraw();

    /// <summary>
    /// Gets or sets the handler that receives the pointer events and answers the cursor query. When it is <see langword="null"/>, the surface ignores pointer input.
    /// </summary>
    ISkiaPointerHandler? Pointer { get; set; }
}
