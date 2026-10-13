using Drm;

namespace Basin.Backend.Drm;

public sealed record DrmInheritedState(
    DrmModeInfo Mode,
    OutputMode OutputMode,
    uint FramebufferId,
    ulong SourceWidth,
    ulong SourceHeight,
    uint CrtcWidth,
    uint CrtcHeight,
    ulong? Colorspace,
    bool HdrMetadata,
    byte HdrEotf,
    ulong? MaxBitsPerColor,
    bool GammaLut,
    bool DegammaLut,
    bool Ctm);
