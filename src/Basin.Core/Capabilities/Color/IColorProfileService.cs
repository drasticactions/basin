namespace Basin.Capabilities;

public interface IColorProfileService
{
    ColorFeatures Features { get; }

    bool TryParseIcc(ReadOnlySpan<byte> profile, out ImageDescription description);

    bool TryBuildParametric(ImageDescription parameters, out ImageDescription description);

    IColorLut? BuildTransform(
        ImageDescription source,
        ImageDescription output,
        IRenderer renderer,
        ColorRenderIntent intent = ColorRenderIntent.Perceptual);

    ColorLut3D? BuildLut(
        ImageDescription source,
        ImageDescription output,
        ColorRenderIntent intent = ColorRenderIntent.Perceptual) => null;

    bool TryDescribeOutput(IOutput output, out ImageDescription description);
}
