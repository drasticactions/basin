namespace Basin.Effects;

/// <summary>
/// Selects the role of a window for the dim-inactive rules.
/// </summary>
public enum DimInactiveWindowKind
{
    /// <summary>
    /// An ordinary application window. It dims when it is inactive.
    /// </summary>
    Normal,

    /// <summary>
    /// A dialog. It dims when it is inactive.
    /// </summary>
    Dialog,

    /// <summary>
    /// A tool or palette window. It dims when it is inactive.
    /// </summary>
    Utility,

    /// <summary>
    /// A panel or dock. It dims only when <see cref="DimInactiveOptions.DimPanels"/> is set.
    /// </summary>
    Panel,

    /// <summary>
    /// The desktop or wallpaper window. It dims only when <see cref="DimInactiveOptions.DimDesktop"/> is set.
    /// </summary>
    Desktop,

    /// <summary>
    /// A menu, tooltip or other popup. It never dims.
    /// </summary>
    Popup,

    /// <summary>
    /// An input method panel. It never dims.
    /// </summary>
    InputMethod,

    /// <summary>
    /// A window that the compositor does not manage, for example an X11 override-redirect window. It never dims.
    /// </summary>
    Unmanaged,
}
