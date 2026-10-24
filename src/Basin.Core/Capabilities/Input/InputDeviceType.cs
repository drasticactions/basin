namespace Basin.Capabilities;

/// <summary>The main type of an input device, which configuration rules can match.</summary>
public enum InputDeviceType
{
    /// <summary>A keyboard.</summary>
    Keyboard,

    /// <summary>A pointer that is not a touchpad, for example a mouse or a trackball.</summary>
    Pointer,

    /// <summary>A touchpad.</summary>
    Touchpad,

    /// <summary>A touchscreen.</summary>
    Touch,

    /// <summary>A tablet that reports a tool, for example a pen.</summary>
    TabletTool,

    /// <summary>The buttons, rings and strips of a tablet.</summary>
    TabletPad,

    /// <summary>A switch, or a device with no other type.</summary>
    Switch,
}
