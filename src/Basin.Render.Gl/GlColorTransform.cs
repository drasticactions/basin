using Basin.Color;

namespace Basin.Render.Gl;

internal sealed class GlColorTransform
{
    internal GlColorTransform(in ColorTransformParameters parameters)
    {
        Parameters = parameters;
        var m = parameters.Matrix;
        M0 = ((float)m[0], (float)m[1], (float)m[2]);
        M1 = ((float)m[3], (float)m[4], (float)m[5]);
        M2 = ((float)m[6], (float)m[7], (float)m[8]);
        Source = ((int)parameters.Source.Kind, (float)parameters.Source.Gamma, (float)parameters.Source.MaxLuminance, (float)parameters.Anchor);
        Output = ((int)parameters.Output.Kind, (float)parameters.Output.Gamma, (float)parameters.Output.MaxLuminance, 0f);
        Tone = (parameters.MapTones ? 1f : 0f, (float)parameters.ToneSourceMax, (float)parameters.ToneTargetMax, (float)parameters.ToneKnee);
    }

    internal ColorTransformParameters Parameters { get; }

    internal (float X, float Y, float Z) M0 { get; }

    internal (float X, float Y, float Z) M1 { get; }

    internal (float X, float Y, float Z) M2 { get; }

    internal (float X, float Y, float Z, float W) Source { get; }

    internal (float X, float Y, float Z, float W) Output { get; }

    internal (float X, float Y, float Z, float W) Tone { get; }
}
