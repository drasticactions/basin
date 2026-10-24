using Pixman;

namespace Basin.Scene;

/// <summary>
/// Tells why a <see cref="SceneOutput"/> did not put a scene node on an overlay plane or on the primary plane.
/// </summary>
public enum PlaneDeclineReason
{
    /// <summary>The node is not a <see cref="SceneBuffer"/> with a buffer.</summary>
    NotABuffer,

    /// <summary>The content needs a color transform that a plane cannot apply.</summary>
    ColorTransform,

    /// <summary>The buffer is not a dmabuf.</summary>
    NoDmabuf,

    /// <summary>The dmabuf has an implicit modifier, so the display cannot know its layout.</summary>
    ImplicitModifier,

    /// <summary>The display cannot scan out the format or modifier of the dmabuf.</summary>
    UnscannableLayout,

    /// <summary>A clip cuts the node, and a plane cannot show the cut.</summary>
    Clipped,

    /// <summary>No part of the node is on the output.</summary>
    OffOutput,

    /// <summary>All the planes that the commit can offer are in use.</summary>
    LayerBudget,

    /// <summary>Composed content above the node leaves a visible part that is not one rectangle.</summary>
    CoveredFromAbove,

    /// <summary>The node has not kept the same plane box for enough commits.</summary>
    Settling,

    /// <summary>The backend refused the same buffer and box before.</summary>
    BackendRefused,

    /// <summary>The backend accepted the layer and then moved it back to composition.</summary>
    Demoted,

    /// <summary>The node has an active backdrop effect.</summary>
    BackdropEffect,

    /// <summary>The node has an alpha below 1, or a transform that is not an axis-aligned scale.</summary>
    Transformed,

    /// <summary>The node has a texture shader, corners, or a brightness or saturation change.</summary>
    PixelShader,

    /// <summary>The node is drawn through a mirror.</summary>
    Mirrored,

    /// <summary>The scaled node does not land on whole output pixels.</summary>
    ScaledFraction,

    /// <summary>The node uses a filter other than linear, and its buffer is cropped or scaled.</summary>
    Filter,

    /// <summary>The backend refused the YUV encoding or range of the buffer.</summary>
    YuvEncoding,

    /// <summary>The renderer blends in linear light, and the node is not opaque.</summary>
    BlendSpace,
}
