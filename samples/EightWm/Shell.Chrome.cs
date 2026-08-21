using Basin;

namespace EightWm;

internal sealed partial class Shell
{
    internal static bool ChromeOpen(ShellView view, ChromePanel except = ChromePanel.None)
    {
        if (except != ChromePanel.Charms && view.Charms is { AnyVisible: true })
        {
            return true;
        }

        if (except != ChromePanel.Title && view.Title is { Visible: true })
        {
            return true;
        }

        return except != ChromePanel.Switcher && view.SwitcherDocked;
    }

    internal void CloseOtherChrome(ShellView view, ChromePanel keep)
    {
        if (keep != ChromePanel.Charms)
        {
            ShowCharms(view, false);
            ClosePane(view);
        }

        if (keep != ChromePanel.Title)
        {
            ShowTitle(view, false);
        }

        if (keep != ChromePanel.Switcher)
        {
            DockSwitcher(view, false);
        }
    }

    internal const string SplitterCursorX = "sb_h_double_arrow";

    internal const string SplitterCursorY = "sb_v_double_arrow";

    internal const string PointerCursor = "left_ptr";

    internal static string SplitterCursor(ShellView view) =>
        view.Host.Portrait ? SplitterCursorY : SplitterCursorX;

    internal static int ChromeKey(ShellView view)
    {
        var key = 0;
        if (view.Splash is { Enabled: true })
        {
            key |= 1;
        }

        if (SplittersLive(view))
        {
            key |= 2;
        }

        if (view.Charms is { AnyVisible: true })
        {
            key |= 4;
        }

        if (view.SwitcherDocked)
        {
            key |= 8;
        }

        if (view.Title is { Visible: true })
        {
            key |= 16;
        }

        if (view.Dim.Enabled)
        {
            key |= 32;
        }

        return view.StartVisible ? key | 64 : key;
    }

    internal void RefreshHot(ShellView view, double localX, double localY)
    {
        HoverCharms(view, localX, localY);
        HotTitle(view, localX, localY);
    }

    internal string? ChromeCursorAt(ShellView view, double localX, double localY, Surface? hit)
    {
        if (view.Splash is { Enabled: true })
        {
            return PointerCursor;
        }

        if (SplittersLive(view) && view.Host.SplitterAt(localX, localY, SplitterSlop) >= 0)
        {
            return SplitterCursor(view);
        }

        if (view.Charms is { AnyVisible: true } charms &&
            ((charms.Visible && Covers(charms.BarBox, localX, localY)) ||
             (charms.OpenPane != Charm.None && Covers(charms.PaneBox, localX, localY))))
        {
            return PointerCursor;
        }

        if (view is { SwitcherDocked: true, Switcher: { } rail } && Covers(rail.Box, localX, localY))
        {
            return PointerCursor;
        }

        if (view.Title is { Visible: true } title && title.Holds(localX, localY))
        {
            return PointerCursor;
        }

        return view.Dim.Enabled && OwnerOf(hit) is not { IsTransient: true } ? PointerCursor : null;
    }

    private static bool Covers(in Box box, double x, double y) =>
        !box.IsEmpty && x >= box.X && y >= box.Y && x < box.Right && y < box.Bottom;

    internal bool ChromePress(ShellView view, double localX, double localY, int touchId)
    {
        if (CharmsPress(view, localX, localY))
        {
            return true;
        }

        if (RailPress(view, localX, localY, touchId))
        {
            return true;
        }

        return TitlePress(view, localX, localY, touchId);
    }

    internal bool ChromeMove(ShellView view, double localX, double localY, int touchId) =>
        RailMove(view, localX, localY, touchId) || TitleMove(view, localX, localY, touchId);

    internal bool ChromeRelease(ShellView view, double localX, double localY, int touchId) =>
        RailRelease(view, localX, localY, touchId) || TitleRelease(view, localX, localY, touchId);

    internal void ChromeCancel()
    {
        RailCancel();
        TitleCancel();
    }
}
