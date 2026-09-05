namespace Basin.Capabilities;

public interface IColorTransformResolver
{
    ColorTransformCapability Capability { get; }

    IColorLut? Resolve(ImageDescription source, ImageDescription output);
}
