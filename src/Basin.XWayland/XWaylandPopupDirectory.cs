using Basin.Capabilities;

namespace Basin.XWayland;

/// <summary>
/// Finds the popups of an X11 window. A transient window belongs to its parent. An override-redirect or menu window belongs to the newest normal window of the same client.
/// </summary>
/// <param name="wm">The window manager whose windows the directory searches.</param>
public sealed class XWaylandPopupDirectory(XWaylandWm wm) : IPopupDirectory
{
    /// <inheritdoc/>
    public void CollectPopups(Surface owner, List<Surface> into)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(into);
        XWaylandWindow? ownerWindow = null;
        foreach (var window in wm.Windows.Values)
        {
            if (window.Surface == owner)
            {
                ownerWindow = window;
                break;
            }
        }

        if (ownerWindow is null)
        {
            return;
        }

        foreach (var window in wm.Windows.Values.OrderBy(static window => window.WindowId))
        {
            if (window != ownerWindow && window.IsMappedInX && window.Surface is { } surface && Owns(ownerWindow, window))
            {
                into.Add(surface);
            }
        }
    }

    private const uint ResourceIdMask = 0x1FFFFF;

    private static bool SameClient(XWaylandWindow a, XWaylandWindow b) =>
        (a.Process.Pid != 0 && a.Process.Pid == b.Process.Pid) || ((a.WindowId ^ b.WindowId) & ~ResourceIdMask) == 0;

    private static bool IsMenu(XWaylandWindow window) =>
        window.WindowType is "menu" or "dropdown_menu" or "popup_menu" or "tooltip" or "combo" or "dnd";

    private bool Owns(XWaylandWindow owner, XWaylandWindow window)
    {
        for (var current = OwnerOf(window); current is not null; current = OwnerOf(current))
        {
            if (current == owner)
            {
                return true;
            }
        }

        return false;
    }

    private XWaylandWindow? OwnerOf(XWaylandWindow window)
    {
        if (window.TransientFor is { } transient)
        {
            return transient;
        }

        if (!window.OverrideRedirect && !IsMenu(window))
        {
            return null;
        }

        XWaylandWindow? latest = null;
        foreach (var candidate in wm.Windows.Values)
        {
            if (!candidate.OverrideRedirect && candidate.IsMappedInX && SameClient(candidate, window)
                && (latest is null || candidate.WindowId > latest.WindowId))
            {
                latest = candidate;
            }
        }

        return latest;
    }
}
