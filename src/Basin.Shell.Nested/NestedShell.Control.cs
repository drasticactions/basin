using Basin.Capabilities;
using Basin.Render.Skia;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Shell.Nested;

/// <content>
/// The toplevel model, capture and control-socket requests of the shell.
/// </content>
public sealed partial class NestedShell
{
    private readonly IToplevelModel? _model;
    private readonly NestedToplevelSource _adopted;
    private SceneCapturePack? _capture;
    private SkiaRenderer? _captureRenderer;
    private XdgToplevelRequestHandler? _xdgHandler;

    /// <summary>
    /// Gets the toplevel model from the host services, or <see langword="null"/> when the host has none.
    /// </summary>
    public IToplevelModel? Toplevels => _model;

    /// <summary>
    /// Gets the capture pack that <see cref="AttachCapture"/> attached, or <see langword="null"/>.
    /// </summary>
    public SceneCapturePack? Capture => _capture;

    /// <summary>
    /// Feeds a capture pack from the shell: the toplevel model, the window trees, the stacking order and the keyboard focus exclusion.
    /// When the pack has no renderer, the shell gives it a raster renderer and disposes it with the shell.
    /// </summary>
    /// <param name="pack">The capture pack.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pack"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The shell is disposed.</exception>
    /// <exception cref="InvalidOperationException">A capture pack is already attached.</exception>
    public void AttachCapture(SceneCapturePack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_capture is not null)
        {
            throw new InvalidOperationException("the shell feeds one capture pack");
        }

        _capture = pack;
        if (_model is { } model)
        {
            pack.Capture.Toplevels = model;
            pack.DmabufCapture.Toplevels = model;
        }

        if (pack.Capture.Renderer is null)
        {
            _captureRenderer = new SkiaRenderer();
            pack.Capture.Renderer = _captureRenderer;
        }

        pack.Exclusion.KeyboardFocusExcluded ??= IsKeyboardFocusExcluded;
        foreach (var window in _windows)
        {
            IndexCapture(window);
        }

        pack.Stack.RaiseChanged();
    }

    /// <summary>
    /// Finds out whether keyboard focus is in content that capture excludes.
    /// </summary>
    /// <returns><see langword="true"/> when the focused UI surface or window content is excluded from capture.</returns>
    public bool IsKeyboardFocusExcluded()
    {
        if (_router.KeyboardFocus is { } chrome)
        {
            return _uiIndex.OwnerOf(chrome) is { } node && Scene.Scene.IsCaptureExcluded(node.Node);
        }

        return _host.Seat.Keyboard.Focus is { } surface && WindowOf(surface)?.ContentNode is { } content
            && Scene.Scene.IsCaptureExcluded(content);
    }

    /// <summary>
    /// Finds the id of a window in <see cref="Toplevels"/> and stores it in <see cref="ManagedWindow.ToplevelId"/>.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The toplevel id, or zero when the model does not list the window.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is <see langword="null"/>.</exception>
    public ulong ToplevelIdOf(ManagedWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.ToplevelId != 0)
        {
            return window.ToplevelId;
        }

        ulong id = 0;
        if (_model is AggregateToplevelModel aggregate)
        {
            if (window.Content is XdgContent xdg && _toplevels is { } source)
            {
                id = aggregate.GlobalId(source, source.IdFor(xdg.Toplevel));
            }
            else if (_adopted.IdOf(window) is var local and not 0)
            {
                id = aggregate.GlobalId(_adopted, local);
            }
        }

        if (id == 0 && _model is { } model && window.Content.Surface is { } surface)
        {
            id = model.TryFind(surface, out var info) ? info.Id : 0;
        }

        window.ToplevelId = id;
        return id;
    }

    /// <summary>
    /// Moves a floating window. It refuses a window that is not mapped, or that is maximized, fullscreen or tiled.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="x">The left edge of the frame in logical layout coordinates.</param>
    /// <param name="y">The top edge of the frame in logical layout coordinates.</param>
    /// <returns><see langword="true"/> when the window moved.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is <see langword="null"/>.</exception>
    public bool MoveFloating(ManagedWindow window, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!IsFloating(window))
        {
            return false;
        }

        window.MoveFrameTo(x, y);
        Publish();
        return true;
    }

    /// <summary>
    /// Asks a floating window for a new content size and keeps its content position. It refuses the cases that <see cref="MoveFloating"/> refuses, and a size that is not positive.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="width">The content width in logical pixels.</param>
    /// <param name="height">The content height in logical pixels.</param>
    /// <returns><see langword="true"/> when the shell asked the content for the size.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is <see langword="null"/>.</exception>
    public bool ResizeFloating(ManagedWindow window, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!IsFloating(window) || width <= 0 || height <= 0)
        {
            return false;
        }

        var client = window.ClientBox;
        window.ResizeClientTo(new Box(client.X, client.Y, width, height), ResizeEdges.None);
        Publish();
        return true;
    }

    internal bool AnswerRequest(ManagedWindow window, in ToplevelRequest request)
    {
        if (!window.IsMapped)
        {
            return false;
        }

        switch (request.Kind)
        {
            case ToplevelRequestKind.Activate:
                ActivateWindow(window);
                return true;
            case ToplevelRequestKind.Close:
                CloseWindow(window);
                return true;
            case ToplevelRequestKind.Maximize or ToplevelRequestKind.Unmaximize:
                SetMaximized(window, request.Kind == ToplevelRequestKind.Maximize);
                return true;
            case ToplevelRequestKind.Minimize or ToplevelRequestKind.Unminimize:
                SetMinimized(window, request.Kind == ToplevelRequestKind.Minimize);
                return true;
            case ToplevelRequestKind.Fullscreen or ToplevelRequestKind.Unfullscreen:
                SetFullscreen(window, request.Kind == ToplevelRequestKind.Fullscreen);
                return true;
            case ToplevelRequestKind.Move:
                return MoveFloating(window, request.Geometry.X, request.Geometry.Y);
            case ToplevelRequestKind.Resize:
                return ResizeFloating(window, request.Geometry.Width, request.Geometry.Height);
            default:
                return false;
        }
    }

    private bool? AnswerXdgRequest(XdgToplevelWindow toplevel, in ToplevelRequest request)
    {
        if (!_byToplevel.TryGetValue(toplevel, out var window) || !window.IsMapped)
        {
            return null;
        }

        return request.Kind is ToplevelRequestKind.Maximize or ToplevelRequestKind.Unmaximize
            or ToplevelRequestKind.Fullscreen or ToplevelRequestKind.Unfullscreen
            or ToplevelRequestKind.Move or ToplevelRequestKind.Resize
            ? AnswerRequest(window, request)
            : null;
    }

    private static bool IsFloating(ManagedWindow window) =>
        window.IsMapped && !window.Maximized && !window.Fullscreen && window.Tile == TileEdge.None;

    private void IndexCapture(ManagedWindow window)
    {
        if (_capture is not { } pack || window.Tree is not { } tree)
        {
            return;
        }

        var id = ToplevelIdOf(window);
        if (id != 0)
        {
            pack.Index.Set(id, new ToplevelCaptureTrees(tree, window.PopupTree, window.ContentNode));
        }
    }

    private void UnindexCapture(ManagedWindow window)
    {
        if (_capture is { } pack && window.ToplevelId != 0)
        {
            pack.Index.Remove(window.ToplevelId);
        }
    }

    private void StackChanged() => _capture?.Stack.RaiseChanged();

    private void DisposeControl()
    {
        if (_toplevels is { } source && _xdgHandler is { } handler && ReferenceEquals(source.RequestHandler, handler))
        {
            source.RequestHandler = null;
        }

        if (_captureRenderer is { } renderer)
        {
            if (_capture is { } pack && ReferenceEquals(pack.Capture.Renderer, renderer))
            {
                pack.Capture.Renderer = null;
            }

            renderer.Dispose();
            _captureRenderer = null;
        }
    }
}
