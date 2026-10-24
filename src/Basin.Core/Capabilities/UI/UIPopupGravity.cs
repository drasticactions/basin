using Pixman;

namespace Basin.Capabilities;

/// <summary>
/// The direction in which a popup extends from its anchor. The values match xdg_positioner.gravity.
/// </summary>
public enum UIPopupGravity
{
    /// <summary>The popup is centered on the anchor.</summary>
    None = 0,
    /// <summary>The popup extends up.</summary>
    Top = 1,
    /// <summary>The popup extends down.</summary>
    Bottom = 2,
    /// <summary>The popup extends to the left.</summary>
    Left = 3,
    /// <summary>The popup extends to the right.</summary>
    Right = 4,
    /// <summary>The popup extends up and to the left.</summary>
    TopLeft = 5,
    /// <summary>The popup extends down and to the left.</summary>
    BottomLeft = 6,
    /// <summary>The popup extends up and to the right.</summary>
    TopRight = 7,
    /// <summary>The popup extends down and to the right.</summary>
    BottomRight = 8,
}
