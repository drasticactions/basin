using Basin.Capabilities;

namespace Basin.Effects;

/// <summary>
/// Holds the facts about one window that the dim-inactive rules read.
/// </summary>
/// <param name="Kind">The role of the window.</param>
/// <param name="Group">The group of the window, or 0 for none. Windows with the same nonzero group stay undimmed together when <see cref="DimInactiveOptions.DimByGroup"/> is set.</param>
/// <param name="KeepAbove">Whether the window stays above other windows.</param>
/// <param name="FullScreen">Whether the window is full screen.</param>
public readonly record struct DimInactiveWindow(
    DimInactiveWindowKind Kind,
    ulong Group = 0,
    bool KeepAbove = false,
    bool FullScreen = false)
{
    private const int MaxParentDepth = 64;

    /// <summary>
    /// Makes the facts of a toplevel. A toplevel with a parent is a dialog, and its group is the id of the toplevel at the top of its parent chain.
    /// </summary>
    /// <param name="toplevels">The toplevel model that resolves the parent chain.</param>
    /// <param name="info">The toplevel.</param>
    /// <returns>The facts. <see cref="KeepAbove"/> is <see langword="false"/>, because the toplevel model does not report it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="toplevels"/> is <see langword="null"/>.</exception>
    public static DimInactiveWindow FromToplevel(IToplevelModel toplevels, in ToplevelInfo info)
    {
        ArgumentNullException.ThrowIfNull(toplevels);
        return new DimInactiveWindow(
            info.ParentId == 0 ? DimInactiveWindowKind.Normal : DimInactiveWindowKind.Dialog,
            GroupOf(toplevels, info),
            FullScreen: (info.State & ToplevelState.Fullscreen) != 0);
    }

    /// <summary>
    /// Maps an X11 <c>_NET_WM_WINDOW_TYPE</c> to a kind. The name is in lowercase without its prefix, for example <c>dock</c> or <c>popup_menu</c>.
    /// </summary>
    /// <param name="windowType">The window type name.</param>
    /// <returns>The kind. A type that is not a normal, dialog, utility, dock or desktop window maps to <see cref="DimInactiveWindowKind.Popup"/>, so it never dims.</returns>
    public static DimInactiveWindowKind KindOfWindowType(string? windowType) => windowType switch
    {
        null or "" or "normal" => DimInactiveWindowKind.Normal,
        "dialog" => DimInactiveWindowKind.Dialog,
        "utility" => DimInactiveWindowKind.Utility,
        "dock" => DimInactiveWindowKind.Panel,
        "desktop" => DimInactiveWindowKind.Desktop,
        _ => DimInactiveWindowKind.Popup,
    };

    /// <summary>
    /// Finds the toplevel at the top of the parent chain of a toplevel. The search stops at a parent that the model does not know.
    /// </summary>
    /// <param name="toplevels">The toplevel model that resolves the parent chain.</param>
    /// <param name="info">The toplevel.</param>
    /// <returns>The id of the top toplevel. It is the id of <paramref name="info"/> when the toplevel has no parent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="toplevels"/> is <see langword="null"/>.</exception>
    public static ulong GroupOf(IToplevelModel toplevels, in ToplevelInfo info)
    {
        ArgumentNullException.ThrowIfNull(toplevels);
        var group = info.Id;
        var parent = info.ParentId;
        for (var depth = 0; parent != 0 && depth < MaxParentDepth; depth++)
        {
            if (!toplevels.TryGet(parent, out var next))
            {
                break;
            }

            group = next.Id;
            parent = next.ParentId;
        }

        return group;
    }
}
