namespace Basin.AppMenu;

/// <summary>A titlebar gesture that a GTK client reports through <c>gtk_surface1.titlebar_gesture</c>.</summary>
public enum GtkShellGesture
{
    /// <summary>A double click on the titlebar.</summary>
    DoubleClick = 1,

    /// <summary>A right click on the titlebar.</summary>
    RightClick = 2,

    /// <summary>A middle click on the titlebar.</summary>
    MiddleClick = 3,
}
