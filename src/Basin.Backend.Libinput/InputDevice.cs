using Basin.Session;
using Libinput;
using Udev;

namespace Basin.Backend.Libinput;

/// <summary>
/// One libinput device. The backend makes a new instance each time libinput adds the device, also after a session switch.
/// </summary>
public sealed class InputDevice
{
    internal InputDevice(LibinputDevice native)
    {
        Native = native;
        Name = native.Name;
        HasKeyboard = native.HasCapability(LibinputDeviceCapability.Keyboard);
        HasPointer = native.HasCapability(LibinputDeviceCapability.Pointer);
        HasTouch = native.HasCapability(LibinputDeviceCapability.Touch);
        HasGesture = native.HasCapability(LibinputDeviceCapability.Gesture);
        HasSwitch = native.HasCapability(LibinputDeviceCapability.Switch);
        HasTabletTool = native.HasCapability(LibinputDeviceCapability.TabletTool);
        HasTabletPad = native.HasCapability(LibinputDeviceCapability.TabletPad);
        Sysname = native.Sysname;
        Vendor = native.IdVendor;
        Product = native.IdProduct;
        Bus = native.IdBustype;
    }

    internal LibinputDevice Native { get; }

    /// <summary>Gets the name that the kernel gives the device.</summary>
    public string Name { get; }

    /// <summary>Gets the kernel event node name, for example <c>event3</c>.</summary>
    public string Sysname { get; }

    /// <summary>Gets the vendor ID from the device's input ID.</summary>
    public uint Vendor { get; }

    /// <summary>Gets the product ID from the device's input ID.</summary>
    public uint Product { get; }

    /// <summary>Gets the bus type from the device's input ID, for example <c>BUS_USB</c>.</summary>
    public uint Bus { get; }

    /// <summary>Gets a value indicating whether libinput gives the device the keyboard capability.</summary>
    public bool HasKeyboard { get; }

    /// <summary>Gets a value indicating whether libinput gives the device the pointer capability.</summary>
    public bool HasPointer { get; }

    /// <summary>Gets a value indicating whether libinput gives the device the touch capability.</summary>
    public bool HasTouch { get; }

    /// <summary>Gets a value indicating whether libinput gives the device the gesture capability. Only touchpads have it.</summary>
    public bool HasGesture { get; }

    /// <summary>Gets a value indicating whether libinput gives the device the switch capability, for example a lid or tablet-mode switch.</summary>
    public bool HasSwitch { get; }

    /// <summary>Gets a value indicating whether libinput gives the device the tablet tool capability.</summary>
    public bool HasTabletTool { get; }

    /// <summary>Gets a value indicating whether libinput gives the device the tablet pad capability.</summary>
    public bool HasTabletPad { get; }

    /// <summary>Gets the libinput configuration of the device. It must not be used after the device is removed.</summary>
    public LibinputDeviceConfig Config => Native.Config;

    /// <summary>Sets the keyboard LEDs of the device.</summary>
    /// <param name="leds">The LEDs to turn on. Every other LED turns off.</param>
    public void UpdateLeds(LibinputLed leds) => Native.UpdateLeds(leds);

    /// <summary>Gets the output name that udev assigns to the device with <c>WL_OUTPUT</c>, or <see langword="null"/>.</summary>
    public string? OutputName => Native.OutputName;
}
