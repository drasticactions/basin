using Basin.Capabilities;

namespace Basin.Color;

/// <summary>
/// The <see cref="IColorProfileService"/> on Little CMS. Without liblcms2 it still handles
/// parametric descriptions and refuses ICC profiles.
/// </summary>
public sealed class Lcms2ColorProfileService : IColorProfileService
{
    private readonly Dictionary<IOutput, ImageDescription> _outputs = [];

    /// <summary>
    /// Gets the supported features. ICC profiles and transforms are in the set only when liblcms2
    /// loads.
    /// </summary>
    public ColorFeatures Features =>
        Lcms2Support.IsAvailable
            ? ColorFeatures.Parametric | ColorFeatures.CustomPrimaries | ColorFeatures.Luminances |
              ColorFeatures.IccProfiles | ColorFeatures.Transforms
            : ColorFeatures.Parametric | ColorFeatures.CustomPrimaries | ColorFeatures.Luminances;

    /// <summary>
    /// Records the description that an output displays, for <see cref="TryDescribeOutput"/>. A later
    /// call for the same output replaces it.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <param name="description">The description of what the output displays.</param>
    public void SetOutputDescription(IOutput output, ImageDescription description)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(description);
        _outputs[output] = description;
    }

    /// <inheritdoc/>
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

    /// <summary>
    /// Accepts a parametric description that names a transfer function and primaries. It returns the
    /// parameters unchanged.
    /// </summary>
    /// <param name="parameters">The description that holds the parameters the client set.</param>
    /// <param name="description">The accepted description, which is <paramref name="parameters"/> itself.</param>
    /// <returns><see langword="true"/> when the parameters name both primaries and a transfer function.</returns>
    public bool TryBuildParametric(ImageDescription parameters, out ImageDescription description)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        description = parameters;

        return (parameters.TransferNamed is not null || parameters.TransferPower is not null)
            && (parameters.PrimariesNamed is not null || parameters.PrimariesCustom is not null);
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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
