using Pixman;

namespace Basin.Scene;

public enum PlaneDeclineReason
{
    NotABuffer,

    ColorTransform,

    NoDmabuf,

    ImplicitModifier,

    Clipped,

    OffOutput,

    LayerBudget,

    CoveredFromAbove,

    Settling,

    BackendRefused,

    Demoted,

    BackdropEffect,

    Transformed,

    PixelShader,

    Mirrored,
}
