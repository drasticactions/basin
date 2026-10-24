namespace Basin.Capabilities;

/// <summary>Tells whether a device supports a setting, and which values it supports.</summary>
/// <param name="Supported"><see langword="true"/> when the device supports the setting.</param>
/// <param name="Mask">For a choice that is a bit value, the bits of the supported choices.</param>
/// <param name="Count">For a tap or drag setting, the number of fingers that the device supports.</param>
public readonly record struct InputSettingSupport(bool Supported, uint Mask = 0, uint Count = 0)
{
    /// <summary>Gets the value for a setting that the device does not support.</summary>
    public static InputSettingSupport None => default;

    /// <summary>Gets the value for a supported setting with no mask and no finger count.</summary>
    public static InputSettingSupport Yes => new(true);
}
