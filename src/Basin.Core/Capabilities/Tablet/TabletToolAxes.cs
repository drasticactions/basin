namespace Basin.Capabilities;

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
