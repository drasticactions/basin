namespace Basin.Capabilities;

/// <summary>How a touchpad decides which button a physical click produces. The values are the libinput bits.</summary>
public enum InputClickMethod : uint
{
    /// <summary>No software click method.</summary>
    None = 0,

    /// <summary>The area of the touchpad that the finger is in selects the button.</summary>
    ButtonAreas = 1,

    /// <summary>The number of fingers on the touchpad selects the button.</summary>
    Clickfinger = 2,
}
