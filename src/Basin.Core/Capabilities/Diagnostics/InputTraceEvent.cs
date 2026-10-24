namespace Basin.Capabilities;

/// <summary>
/// One seat input event that <see cref="IInputDiagnostics.WatchInput"/> reports.
/// </summary>
/// <param name="Kind">The event: "enter", "leave", "motion", "button", "axis", "grab-start", "grab-end" or "keyboard-focus".</param>
/// <param name="Surface">The surface of the event, for example the pointer focus. It can be <see langword="null"/>.</param>
/// <param name="X">The pointer x position in the coordinates of the pointer focus.</param>
/// <param name="Y">The pointer y position in the coordinates of the pointer focus.</param>
/// <param name="LayoutX">The pointer x position in layout coordinates.</param>
/// <param name="LayoutY">The pointer y position in layout coordinates.</param>
/// <param name="Code">The button code for "button", or the axis for "axis". Otherwise 0.</param>
/// <param name="Pressed">For "button", whether the button went down.</param>
/// <param name="Value">For "axis", the axis value.</param>
/// <param name="Detail">The axis source for "axis", the grab type name for a grab event, or <see langword="null"/>.</param>
public readonly record struct InputTraceEvent(
    string Kind,
    Surface? Surface,
    double X,
    double Y,
    double LayoutX,
    double LayoutY,
    uint Code,
    bool Pressed,
    double Value,
    string? Detail);
