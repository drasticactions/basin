using System.Text;
using Basin.Diagnostics;
using Xcb.Native;

namespace Basin.XWayland;

/// <summary>
/// The modifiers of an <see cref="X11HotkeyGrabber"/> chord. Combine values to require more than one modifier.
/// </summary>
[Flags]
public enum X11HotkeyModifiers
{
    /// <summary>
    /// No modifier is held.
    /// </summary>
    None = 0,

    /// <summary>
    /// The Shift modifier.
    /// </summary>
    Shift = 1,

    /// <summary>
    /// The Control modifier.
    /// </summary>
    Ctrl = 2,

    /// <summary>
    /// The Alt modifier, X11 Mod1.
    /// </summary>
    Alt = 4,

    /// <summary>
    /// The Super modifier, X11 Mod4.
    /// </summary>
    Super = 8,
}
