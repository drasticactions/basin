using Basin.Diagnostics;
using Pixman;
using Wayland;

namespace Basin;

/// <summary>
/// The fields that a <see cref="SurfaceState"/> delta sets. Combined flags name several fields, and a commit moves only the fields that are set.
/// </summary>
[Flags]
public enum SurfaceStateFields
{
    /// <summary>No field.</summary>
    None = 0,
    /// <summary>The attached buffer, including a <see langword="null"/> attach.</summary>
    Buffer = 1 << 0,
    /// <summary>The content offset.</summary>
    Offset = 1 << 1,
    /// <summary>The damage in surface coordinates.</summary>
    SurfaceDamage = 1 << 2,
    /// <summary>The damage in buffer pixels.</summary>
    BufferDamage = 1 << 3,
    /// <summary>The opaque region.</summary>
    OpaqueRegion = 1 << 4,
    /// <summary>The input region.</summary>
    InputRegion = 1 << 5,
    /// <summary>The buffer transform.</summary>
    Transform = 1 << 6,
    /// <summary>The buffer scale.</summary>
    Scale = 1 << 7,
    /// <summary>The frame callbacks.</summary>
    FrameCallbacks = 1 << 8,
    /// <summary>The viewport source and destination.</summary>
    Viewport = 1 << 9,
    /// <summary>The per-commit buffer release callback.</summary>
    BufferRelease = 1 << 10,
}
