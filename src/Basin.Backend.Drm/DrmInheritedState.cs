using Drm;

namespace Basin.Backend.Drm;

/// <summary>
/// The display state that a previous DRM master left lit on a connector's CRTC when the backend started.
/// </summary>
/// <param name="Mode">The kernel mode on the CRTC.</param>
/// <param name="OutputMode">The same mode as an <see cref="Basin.OutputMode"/>.</param>
/// <param name="FramebufferId">The framebuffer on the primary plane.</param>
/// <param name="SourceWidth">The primary plane's <c>SRC_W</c>, in 16.16 fixed point, or 0 when unknown.</param>
/// <param name="SourceHeight">The primary plane's <c>SRC_H</c>, in 16.16 fixed point, or 0 when unknown.</param>
/// <param name="CrtcWidth">The primary plane's <c>CRTC_W</c> in pixels, or 0 when unknown.</param>
/// <param name="CrtcHeight">The primary plane's <c>CRTC_H</c> in pixels, or 0 when unknown.</param>
/// <param name="Colorspace">The connector's <c>Colorspace</c> value, or <see langword="null"/> when the connector has no such property.</param>
/// <param name="HdrMetadata">A value indicating whether the connector carries an HDR metadata blob.</param>
/// <param name="HdrEotf">The EOTF byte of the HDR metadata blob, or 0 when there is none.</param>
/// <param name="MaxBitsPerColor">The connector's <c>max bpc</c> value, or <see langword="null"/> when the connector has no such property.</param>
/// <param name="GammaLut">A value indicating whether the CRTC carries a gamma LUT.</param>
/// <param name="DegammaLut">A value indicating whether the CRTC carries a degamma LUT.</param>
/// <param name="Ctm">A value indicating whether the CRTC carries a color transform matrix.</param>
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
