using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Wayland.Server;
using Wayland.Server.Shm;

namespace Basin.Transport.Waypipe;

internal sealed class WaypipeImage : IRemoteImage, IFdSlotPayload
{
    internal WaypipeImage(SharedMemoryRegion region, int width, int height, DrmFormat format, int stride)
    {
        Region = region;
        Width = width;
        Height = height;
        Format = format;
        Stride = stride;
    }

    internal SharedMemoryRegion Region { get; }

    public int Width { get; }

    public int Height { get; }

    public DrmFormat Format { get; }

    public int Stride { get; }

    public nint Pixels
    {
        get
        {
            unsafe
            {
                return (nint)Unsafe.AsPointer(ref MemoryMarshal.GetReference(Region.Span));
            }
        }
    }

    public bool IsReleased => Region.IsReleased;

    public void AddRef() => Region.AddRef();

    public void Release() => Region.Release();
}
