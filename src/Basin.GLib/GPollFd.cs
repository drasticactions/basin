using System.Runtime.InteropServices;

namespace Basin.GLib;

[StructLayout(LayoutKind.Sequential)]
internal struct GPollFd
{
    public int Fd;
    public ushort Events;
    public ushort Revents;
}
