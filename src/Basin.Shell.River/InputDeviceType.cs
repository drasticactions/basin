using Basin.Shell.River.Protocol;
using Wayland.Server;

namespace Basin.Shell.River;

/// <summary>
/// The kind of input device that <see cref="RiverInputManager.AddDevice(object, string, InputDeviceType)"/> announces to the window manager.
/// The values match the river-input-management-v1 wire values.
/// </summary>
public enum InputDeviceType
{
    /// <summary>A keyboard.</summary>
    Keyboard = 0,

    /// <summary>A pointer device, for example a mouse or a touchpad.</summary>
    Pointer = 1,

    /// <summary>A touchscreen.</summary>
    Touch = 2,

    /// <summary>A drawing tablet or a pen.</summary>
    Tablet = 3,
}
