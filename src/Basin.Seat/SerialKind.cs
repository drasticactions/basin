using Basin.Diagnostics;
using Wayland;
using Wayland.Server;

namespace Basin.Seat;

/// <summary>
/// The kind of event that a seat serial went with. The serial checks of <see cref="Seat"/> read it.
/// </summary>
public enum SerialKind
{
    /// <summary>Any event that cannot authorize a grab or a cursor change.</summary>
    Other,
    /// <summary>A pointer enter.</summary>
    PointerEnter,
    /// <summary>A pointer button press.</summary>
    PointerButtonPress,
    /// <summary>A pointer button release.</summary>
    PointerButtonRelease,
    /// <summary>A keyboard enter.</summary>
    KeyboardEnter,
    /// <summary>A key press.</summary>
    KeyPress,
    /// <summary>A key release.</summary>
    KeyRelease,
    /// <summary>A touch down.</summary>
    TouchDown,
}
