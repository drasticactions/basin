namespace Basin.Capabilities;

[Flags]
public enum InputDeviceCapability
{
    None = 0,
    Keyboard = 1,
    Pointer = 2,
    Touch = 4,
    Gesture = 8,
    Switch = 16,
    TabletTool = 32,
    TabletPad = 64,
}
