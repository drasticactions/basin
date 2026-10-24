namespace Basin.Capabilities;

/// <summary>Derives an <see cref="InputDeviceType"/> from the capabilities of a device.</summary>
public static class InputDeviceTypes
{
    /// <summary>
    /// Gets the main type of a device. Tablet tool, tablet pad, touch, pointer and keyboard win in that order.
    /// A pointer with tap fingers is a touchpad.
    /// </summary>
    /// <param name="capabilities">The capabilities of the device.</param>
    /// <param name="tapFingerCount">The number of fingers that the device supports for tapping.</param>
    /// <returns>The type. A device with none of these capabilities gives <see cref="InputDeviceType.Switch"/>.</returns>
    public static InputDeviceType Of(InputDeviceCapability capabilities, int tapFingerCount)
    {
        if ((capabilities & InputDeviceCapability.TabletTool) != 0)
        {
            return InputDeviceType.TabletTool;
        }

        if ((capabilities & InputDeviceCapability.TabletPad) != 0)
        {
            return InputDeviceType.TabletPad;
        }

        if ((capabilities & InputDeviceCapability.Touch) != 0)
        {
            return InputDeviceType.Touch;
        }

        if ((capabilities & InputDeviceCapability.Pointer) != 0)
        {
            return tapFingerCount > 0 ? InputDeviceType.Touchpad : InputDeviceType.Pointer;
        }

        if ((capabilities & InputDeviceCapability.Keyboard) != 0)
        {
            return InputDeviceType.Keyboard;
        }

        return InputDeviceType.Switch;
    }
}
