namespace Basin.Capabilities;

/// <summary>A pointer acceleration profile. The values are the libinput bits, so a support mask can combine them.</summary>
public enum InputAccelProfile : uint
{
    /// <summary>No acceleration profile.</summary>
    None = 0,

    /// <summary>A constant factor that does not depend on the speed of the movement.</summary>
    Flat = 1,

    /// <summary>A factor that increases with the speed of the movement.</summary>
    Adaptive = 2,

    /// <summary>A curve that <see cref="IInputDeviceConfiguration.SetAccelCurve"/> defines.</summary>
    Custom = 4,
}
