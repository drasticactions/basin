namespace Basin.Capabilities;

/// <summary>
/// Holds the axis values of a tablet tool at one instant. An axis that the tool lacks holds 0.
/// </summary>
/// <param name="X">The horizontal position from 0 to 1 across the active area of the tablet.</param>
/// <param name="Y">The vertical position from 0 to 1 across the active area of the tablet.</param>
/// <param name="Pressure">The tip pressure from 0 to 1.</param>
/// <param name="Distance">The hover distance from 0 to 1.</param>
/// <param name="TiltX">The tilt along the X axis in degrees.</param>
/// <param name="TiltY">The tilt along the Y axis in degrees.</param>
/// <param name="Rotation">The rotation of the tool in degrees, clockwise from its rest position.</param>
/// <param name="Slider">The position of the tool slider from -1 to 1.</param>
/// <param name="Wheel">The wheel movement since the last event, in degrees.</param>
public readonly record struct TabletToolAxes(
    double X,
    double Y,
    double Pressure,
    double Distance,
    double TiltX,
    double TiltY,
    double Rotation,
    double Slider,
    double Wheel);
