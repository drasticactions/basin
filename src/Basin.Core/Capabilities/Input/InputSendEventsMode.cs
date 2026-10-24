namespace Basin.Capabilities;

/// <summary>Whether a device sends events. The values match libinput.</summary>
public enum InputSendEventsMode : uint
{
    /// <summary>The device sends events.</summary>
    Enabled = 0,

    /// <summary>The device sends no events.</summary>
    Disabled = 1,

    /// <summary>The device sends no events while an external mouse is connected.</summary>
    DisabledOnExternalMouse = 2,
}
