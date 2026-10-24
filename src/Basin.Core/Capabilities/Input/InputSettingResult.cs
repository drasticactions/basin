namespace Basin.Capabilities;

/// <summary>The outcome of a change to a device setting.</summary>
public enum InputSettingResult
{
    /// <summary>The device uses the new value.</summary>
    Success,

    /// <summary>The device or the configuration service does not support the setting.</summary>
    Unsupported,

    /// <summary>The device is unknown, or the device refused the value.</summary>
    Invalid,
}
