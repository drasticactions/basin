using Basin.Capabilities;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Shell.Nested;

/// <summary>
/// The content of a window that <see cref="NestedShell"/> manages: an xdg toplevel, or content that a consumer adopts.
/// The shell draws the frame and decides the geometry. The content reports its state and applies the requests.
/// </summary>
public interface IWindowContent
{
    /// <summary>
    /// Gets the window title. It can be empty.
    /// </summary>
    string Title { get; }

    /// <summary>
    /// Gets the application id. It can be empty.
    /// </summary>
    string AppId { get; }

    /// <summary>
    /// Gets the visible content box relative to the origin of the scene node from <see cref="Attach"/>, in logical pixels.
    /// </summary>
    Box Geometry { get; }

    /// <summary>
    /// Gets the smallest width that the content accepts, or zero for no limit.
    /// </summary>
    int MinWidth { get; }

    /// <summary>
    /// Gets the smallest height that the content accepts, or zero for no limit.
    /// </summary>
    int MinHeight { get; }

    /// <summary>
    /// Gets the largest width that the content accepts, or zero for no limit.
    /// </summary>
    int MaxWidth { get; }

    /// <summary>
    /// Gets the largest height that the content accepts, or zero for no limit.
    /// </summary>
    int MaxHeight { get; }

    /// <summary>
    /// Gets the content that this content is a transient of, or <see langword="null"/>. Content with a parent is a dialog.
    /// </summary>
    IWindowContent? Parent { get; }

    /// <summary>
    /// Gets the Wayland surface that gets keyboard focus and screen enter events, or <see langword="null"/> when the content has none.
    /// </summary>
    Surface? Surface { get; }

    /// <summary>
    /// Gets the consumer UI surface that gets keyboard focus in place of <see cref="Surface"/>, or <see langword="null"/>.
    /// </summary>
    IUISurface? UISurface { get; }

    /// <summary>
    /// Gets a value indicating whether the content is in the maximized state now.
    /// </summary>
    bool Maximized { get; }

    /// <summary>
    /// Gets a value indicating whether the content is in the fullscreen state now.
    /// </summary>
    bool Fullscreen { get; }

    /// <summary>
    /// Gets a value indicating whether an interactive resize is in progress on the content.
    /// </summary>
    bool Resizing { get; }

    /// <summary>
    /// Gets a value indicating whether the shell must draw a frame around the content.
    /// </summary>
    bool ServerDecorated { get; }

    /// <summary>
    /// Gets a value indicating whether the content maps centered on the work area at its own size, for example a prompt.
    /// </summary>
    bool Centered { get; }

    /// <summary>
    /// Gets the frame buttons and window menu entries that the content supports.
    /// </summary>
    FrameCapabilities Capabilities { get; }

    /// <summary>
    /// Occurs when <see cref="Title"/> changes.
    /// </summary>
    event Action? TitleChanged;

    /// <summary>
    /// Occurs when <see cref="AppId"/> changes.
    /// </summary>
    event Action? AppIdChanged;

    /// <summary>
    /// Occurs when <see cref="Parent"/> changes.
    /// </summary>
    event Action? ParentChanged;

    /// <summary>
    /// Occurs when <see cref="ServerDecorated"/> changes. The shell then adds or removes the frame.
    /// </summary>
    event Action? DecorationsChanged;

    /// <summary>
    /// Occurs when the content applies a new state or size. The shell lays out the frame again.
    /// </summary>
    event Action? Committed;

    /// <summary>
    /// Creates the scene node of the content under a window tree. The shell calls it once when the window maps.
    /// </summary>
    /// <param name="tree">The window tree. Its origin is the window position.</param>
    /// <returns>The content node, which the shell keeps above the frame.</returns>
    SceneNode Attach(SceneTree tree);

    /// <summary>
    /// Destroys the scene node from <see cref="Attach"/>. The shell calls it when the window unmaps.
    /// </summary>
    void Detach();

    /// <summary>
    /// Tells the content whether it has focus.
    /// </summary>
    /// <param name="activated"><see langword="true"/> when the window has focus.</param>
    void SetActivated(bool activated);

    /// <summary>
    /// Sets the maximized state. The shell sets the size in a separate call.
    /// </summary>
    /// <param name="maximized"><see langword="true"/> to maximize.</param>
    void SetMaximized(bool maximized);

    /// <summary>
    /// Sets the fullscreen state. The shell sets the size in a separate call.
    /// </summary>
    /// <param name="fullscreen"><see langword="true"/> for fullscreen.</param>
    void SetFullscreen(bool fullscreen);

    /// <summary>
    /// Tells the content that an interactive resize starts or ends.
    /// </summary>
    /// <param name="resizing"><see langword="true"/> while the resize is in progress.</param>
    void SetResizing(bool resizing);

    /// <summary>
    /// Tells the content that it is minimized or restored.
    /// </summary>
    /// <param name="minimized"><see langword="true"/> when the window is minimized.</param>
    void SetMinimized(bool minimized);

    /// <summary>
    /// Tells the content that the shell raised it, for content that keeps a stacking order of its own.
    /// </summary>
    void Raise();

    /// <summary>
    /// Tells the content that the shell lowered it, for content that keeps a stacking order of its own.
    /// </summary>
    void Lower();

    /// <summary>
    /// Sets the edges that are tiled against a neighbor or the work area edge.
    /// </summary>
    /// <param name="edges">The tiled edges, or <see cref="ResizeEdges.None"/> for a floating window.</param>
    void SetTiled(ResizeEdges edges);

    /// <summary>
    /// Asks the content for a new size. The content applies it on a later commit.
    /// </summary>
    /// <param name="width">The content width in logical pixels, or zero to let the content choose.</param>
    /// <param name="height">The content height in logical pixels, or zero to let the content choose.</param>
    void SetSize(int width, int height);

    /// <summary>
    /// Gives the largest size that is useful for the content, which is the work area size.
    /// </summary>
    /// <param name="width">The largest useful width in logical pixels.</param>
    /// <param name="height">The largest useful height in logical pixels.</param>
    void SetBounds(int width, int height);

    /// <summary>
    /// Tells the content where its visible box is, for content that tracks its own position.
    /// </summary>
    /// <param name="x">The left edge of the content box in logical layout coordinates.</param>
    /// <param name="y">The top edge of the content box in logical layout coordinates.</param>
    void SetPosition(int x, int y);

    /// <summary>
    /// Tells the content that the output scale changed.
    /// </summary>
    /// <param name="scale">The new output scale.</param>
    void OutputScaleChanged(double scale);

    /// <summary>
    /// Reports where the window is after each move, resize or frame change.
    /// </summary>
    /// <param name="frame">The frame box in logical layout coordinates.</param>
    /// <param name="client">The content box in logical layout coordinates.</param>
    void ReportGeometry(in Box frame, in Box client);

    /// <summary>
    /// Asks the content to close. The window stays until the content unmaps.
    /// </summary>
    void Close();

    /// <summary>
    /// Finds out whether a surface belongs to this content, subsurfaces included.
    /// </summary>
    /// <param name="surface">The surface to test.</param>
    /// <returns><see langword="true"/> when the surface is part of the content.</returns>
    bool Owns(Surface surface);
}
