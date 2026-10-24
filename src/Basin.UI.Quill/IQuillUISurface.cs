using Basin.Capabilities;
using Prowl.Quill;

namespace Basin.UI.Quill;

/// <summary>
/// A UI surface that Prowl.Quill draws on the GPU. A caller draws with the surface canvas through <see cref="BeginDraw"/>, or with its own canvas on <see cref="Renderer"/> between <see cref="BeginTarget"/> and <see cref="EndTarget"/>.
/// </summary>
public interface IQuillUISurface : IUISurface
{
    /// <summary>
    /// Gets the renderer that draws into the target of the current frame. A caller with its own canvas builds the canvas on it, and must not also use <see cref="BeginDraw"/>.
    /// </summary>
    ICanvasRenderer Renderer { get; }

    /// <summary>
    /// Gets the glyph atlas settings that the surface canvas uses.
    /// </summary>
    FontAtlasSettings Atlas { get; }

    /// <summary>
    /// Starts a frame: it binds the next buffer, waits for its readers and clears it to transparent. Call <see cref="EndTarget"/> to finish the frame.
    /// </summary>
    void BeginTarget();

    /// <summary>
    /// Finishes the frame that <see cref="BeginTarget"/> started, publishes its write fence and makes it ready for <see cref="IUISurface.TryAcquire"/>.
    /// </summary>
    void EndTarget();

    /// <summary>
    /// Starts a frame with <see cref="BeginTarget"/> and begins a frame of the surface canvas in logical pixels.
    /// </summary>
    /// <returns>The surface canvas. It is valid only until <see cref="EndDraw"/>.</returns>
    Canvas BeginDraw();

    /// <summary>
    /// Renders the surface canvas and finishes the frame with <see cref="EndTarget"/>.
    /// </summary>
    void EndDraw();
}
