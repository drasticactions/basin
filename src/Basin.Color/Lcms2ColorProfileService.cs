using Basin.Capabilities;

namespace Basin.Color;

public sealed class Lcms2ColorProfileService : IColorProfileService
{
    private readonly Dictionary<IOutput, ImageDescription> _outputs = [];

    public ColorFeatures Features =>
        Lcms2Support.IsAvailable
            ? ColorFeatures.Parametric | ColorFeatures.CustomPrimaries | ColorFeatures.Luminances |
              ColorFeatures.IccProfiles | ColorFeatures.Transforms
            : ColorFeatures.Parametric | ColorFeatures.CustomPrimaries | ColorFeatures.Luminances;

    public void SetOutputDescription(IOutput output, ImageDescription description)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(description);
        _outputs[output] = description;
    }

    public bool TryParseIcc(ReadOnlySpan<byte> profile, out ImageDescription description)
    {
        description = ImageDescription.SdrDefault;
        if (!Lcms2Support.IsAvailable)
        {
            return false;
        }

        var data = profile.ToArray();
        if (!IccProfiles.Validate(data))
        {
            return false;
        }

        description = new ImageDescription { IccData = data };
        return true;
    }

    public bool TryBuildParametric(ImageDescription parameters, out ImageDescription description)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        description = parameters;

        return (parameters.TransferNamed is not null || parameters.TransferPower is not null)
            && (parameters.PrimariesNamed is not null || parameters.PrimariesCustom is not null);
    }

    public IColorLut? BuildTransform(
        ImageDescription source,
        ImageDescription output,
        IRenderer renderer,
        ColorRenderIntent intent = ColorRenderIntent.Perceptual)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(renderer);

        if (renderer.ColorTransform == ColorTransformCapability.None || ColorLutBaker.IsIdentity(source, output))
        {
            return null;
        }

        var lut = BuildLut(source, output, intent);
        return lut is null ? null : renderer.ImportLut(lut);
    }

    public ColorLut3D? BuildLut(
        ImageDescription source,
        ImageDescription output,
        ColorRenderIntent intent = ColorRenderIntent.Perceptual)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);

        if (ColorLutBaker.IsIdentity(source, output))
        {
            return null;
        }

        return source.IccData is { } icc
            ? ColorLutBaker.BakeFromIcc(icc, output, ColorLutBaker.DefaultSize, intent)
            : ColorLutBaker.Bake(source, output, ColorLutBaker.DefaultSize, intent);
    }

    public bool TryDescribeOutput(IOutput output, out ImageDescription description)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (_outputs.TryGetValue(output, out var known))
        {
            description = known;
            return true;
        }

        description = ImageDescription.SdrDefault;
        return false;
    }
}
