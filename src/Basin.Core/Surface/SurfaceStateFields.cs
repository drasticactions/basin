using Basin.Diagnostics;
using Pixman;
using Wayland;

namespace Basin;

[Flags]
public enum SurfaceStateFields
{
    None = 0,
    Buffer = 1 << 0,
    Offset = 1 << 1,
    SurfaceDamage = 1 << 2,
    BufferDamage = 1 << 3,
    OpaqueRegion = 1 << 4,
    InputRegion = 1 << 5,
    Transform = 1 << 6,
    Scale = 1 << 7,
    FrameCallbacks = 1 << 8,
    Viewport = 1 << 9,
    BufferRelease = 1 << 10,
}
