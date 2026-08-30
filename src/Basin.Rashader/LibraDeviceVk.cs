using System.Runtime.InteropServices;

namespace Basin.Rashader;

[StructLayout(LayoutKind.Sequential)]
internal struct LibraDeviceVk
{
    public nint PhysicalDevice;
    public nint Instance;
    public nint Device;
    public nint Queue;
    public nint Entry;
}
