namespace Basin.Capabilities;

/// <summary>The kinds of input that a device produces. A device can combine more than one.</summary>
[Flags]
public enum InputDeviceCapability
{
    /// <summary>No kind of input.</summary>
    None = 0,
    /// <summary>Key events.</summary>
    Keyboard = 1,
    /// <summary>Pointer motion, button and scroll events.</summary>
    Pointer = 2,
    /// <summary>Touch events.</summary>
    Touch = 4,
    /// <summary>Touchpad gesture events, for example swipe and pinch.</summary>
    Gesture = 8,
    /// <summary>Switch events, for example a lid or a tablet-mode switch.</summary>
    Switch = 16,
    /// <summary>Tablet tool events, for example from a pen.</summary>
    TabletTool = 32,
    /// <summary>Tablet pad button, ring and strip events.</summary>
    TabletPad = 64,
}
