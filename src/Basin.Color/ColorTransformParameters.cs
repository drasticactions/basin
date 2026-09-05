using Basin.Capabilities;

namespace Basin.Color;

public readonly struct ColorTransformParameters
{
    private readonly double[] _matrix;

    private ColorTransformParameters(
        TransferCharacteristics source,
        TransferCharacteristics output,
        double[] matrix,
        double anchor,
        bool mapTones,
        double sourcePeak)
    {
        Source = source;
        Output = output;
        _matrix = matrix;
        Anchor = anchor;
        MapTones = mapTones;
        ToneSourceMax = mapTones ? TransferCharacteristics.PqInverseEotf(sourcePeak) : 1;
        ToneTargetMax = mapTones ? TransferCharacteristics.PqInverseEotf(output.MaxLuminance) / ToneSourceMax : 1;
        ToneKnee = mapTones ? Math.Max(0, 1.5 * ToneTargetMax - 0.5) : 1;
    }

    public TransferCharacteristics Source { get; }

    public TransferCharacteristics Output { get; }

    public ReadOnlySpan<double> Matrix => _matrix;

    public double Anchor { get; }

    public bool MapTones { get; }

    public double ToneSourceMax { get; }

    public double ToneTargetMax { get; }

    public double ToneKnee { get; }

    public static ColorTransformParameters From(
        ImageDescription source,
        ImageDescription output,
        ColorRenderIntent intent = ColorRenderIntent.Perceptual)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);
        var sourceTc = TransferCharacteristics.From(source);
        var outputTc = TransferCharacteristics.From(output);
        var matrix = Colorimetry.GamutMatrix(
            Chromaticities.From(source),
            Chromaticities.From(output),
            adaptWhite: intent != ColorRenderIntent.AbsoluteNoAdaptation);
        var anchor = outputTc.ReferenceLuminance / sourceTc.ReferenceLuminance;
        var peak = sourceTc.MaxLuminance * anchor;
        var mapTones = peak > outputTc.MaxLuminance * 1.001;
        return new ColorTransformParameters(sourceTc, outputTc, matrix, anchor, mapTones, peak);
    }

    public void Apply(Span<double> rgb)
    {
        rgb[0] = Source.Decode(rgb[0]) * Anchor;
        rgb[1] = Source.Decode(rgb[1]) * Anchor;
        rgb[2] = Source.Decode(rgb[2]) * Anchor;
        Convert(rgb);
        if (MapTones)
        {
            ToneMap(rgb);
        }

        rgb[0] = Output.Encode(rgb[0]);
        rgb[1] = Output.Encode(rgb[1]);
        rgb[2] = Output.Encode(rgb[2]);
    }

    private void Convert(Span<double> rgb)
    {
        var m = _matrix;
        var r = m[0] * rgb[0] + m[1] * rgb[1] + m[2] * rgb[2];
        var g = m[3] * rgb[0] + m[4] * rgb[1] + m[5] * rgb[2];
        var b = m[6] * rgb[0] + m[7] * rgb[1] + m[8] * rgb[2];
        rgb[0] = Math.Max(0, r);
        rgb[1] = Math.Max(0, g);
        rgb[2] = Math.Max(0, b);
    }

    private void ToneMap(Span<double> rgb)
    {
        var max = Math.Max(rgb[0], Math.Max(rgb[1], rgb[2]));
        if (max <= 0)
        {
            return;
        }

        var scale = Bt2390(max) / max;
        rgb[0] *= scale;
        rgb[1] *= scale;
        rgb[2] *= scale;
    }

    private double Bt2390(double nits)
    {
        var e1 = Math.Min(1, TransferCharacteristics.PqInverseEotf(nits) / ToneSourceMax);
        if (e1 <= ToneKnee)
        {
            return nits;
        }

        var t = (e1 - ToneKnee) / (1 - ToneKnee);
        var t2 = t * t;
        var t3 = t2 * t;
        var e2 = (2 * t3 - 3 * t2 + 1) * ToneKnee
            + (t3 - 2 * t2 + t) * (1 - ToneKnee)
            + (-2 * t3 + 3 * t2) * ToneTargetMax;
        return TransferCharacteristics.PqEotf(e2 * ToneSourceMax);
    }
}
