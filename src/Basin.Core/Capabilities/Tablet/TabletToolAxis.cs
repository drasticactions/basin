namespace Basin.Capabilities;

[Flags]
public enum TabletToolAxis : uint
{
    None = 0,
    Tilt = 1,
    Pressure = 2,
    Distance = 4,
    Rotation = 8,
    Slider = 16,
    Wheel = 32,
}
