namespace Basin.Capabilities;

/// <summary>
/// The actions that a <see cref="ToplevelRequest"/> can ask for.
/// </summary>
public enum ToplevelRequestKind
{
    /// <summary>Activate and raise the toplevel.</summary>
    Activate,
    /// <summary>Ask the client to close the toplevel.</summary>
    Close,
    /// <summary>Maximize the toplevel.</summary>
    Maximize,
    /// <summary>Restore a maximized toplevel.</summary>
    Unmaximize,
    /// <summary>Minimize the toplevel.</summary>
    Minimize,
    /// <summary>Restore a minimized toplevel.</summary>
    Unminimize,
    /// <summary>Make the toplevel fullscreen.</summary>
    Fullscreen,
    /// <summary>End the fullscreen state of the toplevel.</summary>
    Unfullscreen,
    /// <summary>Remove the server-side decorations of the toplevel.</summary>
    SetNoBorder,
    /// <summary>Restore the server-side decorations of the toplevel.</summary>
    UnsetNoBorder,
    /// <summary>Keep the toplevel out of screen captures.</summary>
    ExcludeFromCapture,
    /// <summary>Show the toplevel in screen captures again.</summary>
    IncludeInCapture,
    /// <summary>Move the toplevel to <see cref="ToplevelRequest.Output"/>.</summary>
    SendToOutput,
    /// <summary>Set the minimize target box to <see cref="ToplevelRequest.Geometry"/>, relative to <see cref="ToplevelRequest.Surface"/>.</summary>
    SetMinimizedGeometry,
    /// <summary>Clear the minimize target box.</summary>
    UnsetMinimizedGeometry,
    /// <summary>Move the toplevel to the position of <see cref="ToplevelRequest.Geometry"/> in layout coordinates.</summary>
    Move,
    /// <summary>Resize the toplevel to the size of <see cref="ToplevelRequest.Geometry"/> in logical pixels.</summary>
    Resize,
}
