namespace Basin.Capabilities;

/// <summary>
/// Describes a change of one control on a tablet pad.
/// </summary>
/// <param name="Kind">The kind of control that changed.</param>
/// <param name="Group">The mode of the pad group when the event happened.</param>
/// <param name="Index">The number of the button, ring, strip or dial.</param>
/// <param name="Value">The ring position in degrees, the strip position from 0 to 1, or the dial delta in 120ths of a detent. It is 0 for a button.</param>
/// <param name="Pressed">A value indicating whether a button is pressed. It is <see langword="false"/> for the other kinds.</param>
public readonly record struct TabletPadEvent(
    TabletPadEventKind Kind,
    uint Group,
    uint Index,
    double Value,
    bool Pressed);
