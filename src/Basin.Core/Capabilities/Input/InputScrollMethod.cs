namespace Basin.Capabilities;

/// <summary>How a device produces scroll events. The values are the libinput bits.</summary>
public enum InputScrollMethod : uint
{
    /// <summary>The device does not scroll in software.</summary>
    None = 0,

    /// <summary>Two fingers move on a touchpad.</summary>
    TwoFinger = 1,

    /// <summary>One finger moves along the edge of a touchpad.</summary>
    Edge = 2,

    /// <summary>Pointer motion scrolls while the scroll button is held.</summary>
    OnButtonDown = 4,
}
