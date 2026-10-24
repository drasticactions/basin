namespace Basin.Capabilities;

/// <summary>
/// The window type that a theme can draw differently.
/// </summary>
public enum FrameKind
{
    /// <summary>
    /// An ordinary application window.
    /// </summary>
    Normal,
    /// <summary>
    /// A dialog.
    /// </summary>
    Dialog,
    /// <summary>
    /// A dialog that blocks its parent.
    /// </summary>
    ModalDialog,
    /// <summary>
    /// A utility window, for example a tool palette. Themes often draw a smaller title bar.
    /// </summary>
    Utility,
    /// <summary>
    /// A torn-off menu.
    /// </summary>
    Menu,
    /// <summary>
    /// A window with a bare border and no title bar.
    /// </summary>
    Border,
    /// <summary>
    /// A modal dialog drawn as attached to its parent window.
    /// </summary>
    Attached,
}
