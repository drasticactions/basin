namespace Basin.Capabilities;

public enum ColorRenderIntent : uint
{
    Perceptual = 0,
    Relative = 1,
    Saturation = 2,
    Absolute = 3,
    RelativeBpc = 4,

    AbsoluteNoAdaptation = 5,
}
