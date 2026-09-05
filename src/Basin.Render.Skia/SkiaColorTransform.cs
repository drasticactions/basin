using Basin.Color;
using SkiaSharp;

namespace Basin.Render.Skia;

internal sealed class SkiaColorTransform : IDisposable
{
    private const string Sksl = """
        uniform float3 m0;
        uniform float3 m1;
        uniform float3 m2;
        uniform float4 source;
        uniform float4 output_;
        uniform float4 tone;
        const float PQ_M1 = 2610.0 / 16384.0;
        const float PQ_M2 = 2523.0 / 4096.0 * 128.0;
        const float PQ_C1 = 3424.0 / 4096.0;
        const float PQ_C2 = 2413.0 / 4096.0 * 32.0;
        const float PQ_C3 = 2392.0 / 4096.0 * 32.0;
        const float HLG_A = 0.17883277;
        const float HLG_B = 0.28466892;
        const float HLG_C = 0.55991073;
        float3 srgb_to_linear(float3 c) {
            return mix(c / 12.92, pow((c + 0.055) / 1.055, float3(2.4)), step(0.04045, c));
        }
        float3 compound_inverse(float3 l) {
            return mix(l * 12.92, 1.055 * pow(l, float3(1.0 / 2.4)) - 0.055, step(0.0031308, l));
        }
        float3 pq_eotf(float3 s) {
            float3 p = pow(s, float3(1.0 / PQ_M2));
            float3 num = max(p - PQ_C1, float3(0.0));
            return 10000.0 * pow(num / (PQ_C2 - PQ_C3 * p), float3(1.0 / PQ_M1));
        }
        float pq_eotf1(float s) {
            float p = pow(s, 1.0 / PQ_M2);
            float num = max(p - PQ_C1, 0.0);
            return 10000.0 * pow(num / (PQ_C2 - PQ_C3 * p), 1.0 / PQ_M1);
        }
        float3 pq_inverse(float3 nits) {
            float3 y = pow(clamp(nits / 10000.0, 0.0, 1.0), float3(PQ_M1));
            return pow((PQ_C1 + PQ_C2 * y) / (1.0 + PQ_C3 * y), float3(PQ_M2));
        }
        float pq_inverse1(float nits) {
            float y = pow(clamp(nits / 10000.0, 0.0, 1.0), PQ_M1);
            return pow((PQ_C1 + PQ_C2 * y) / (1.0 + PQ_C3 * y), PQ_M2);
        }
        float3 hlg_inverse_oetf(float3 s) {
            return mix(s * s / 3.0, (exp((s - HLG_C) / HLG_A) + HLG_B) / 12.0, step(0.5, s));
        }
        float3 hlg_oetf(float3 e) {
            return mix(sqrt(3.0 * e), HLG_A * log(max(12.0 * e - HLG_B, 1e-6)) + HLG_C, step(1.0 / 12.0, e));
        }
        float3 decode(float4 tf, float3 s) {
            int kind = int(tf.x);
            if (kind == 0) return srgb_to_linear(s) * tf.z;
            if (kind == 1) return pow(s, float3(tf.y)) * tf.z;
            if (kind == 2) return s * tf.z;
            if (kind == 3) return pq_eotf(s);
            return pow(hlg_inverse_oetf(s), float3(1.2)) * tf.z;
        }
        float3 encode(float4 tf, float3 nits) {
            int kind = int(tf.x);
            nits = max(nits, float3(0.0));
            float3 relative = min(nits / tf.z, float3(1.0));
            if (kind == 0) return compound_inverse(relative);
            if (kind == 1) return pow(relative, float3(1.0 / tf.y));
            if (kind == 2) return relative;
            if (kind == 3) return pq_inverse(nits);
            return hlg_oetf(pow(relative, float3(1.0 / 1.2)));
        }
        float bt2390(float nits) {
            float e1 = min(1.0, pq_inverse1(nits) / tone.y);
            if (e1 <= tone.w) return nits;
            float t = (e1 - tone.w) / (1.0 - tone.w);
            float t2 = t * t;
            float t3 = t2 * t;
            float e2 = (2.0 * t3 - 3.0 * t2 + 1.0) * tone.w
                + (t3 - 2.0 * t2 + t) * (1.0 - tone.w)
                + (-2.0 * t3 + 3.0 * t2) * tone.z;
            return pq_eotf1(e2 * tone.y);
        }
        half4 main(half4 c) {
            float a = float(c.a);
            float3 straight = a > 0.0 ? clamp(float3(c.rgb) / a, 0.0, 1.0) : float3(0.0);
            float3 nits = decode(source, straight) * source.w;
            nits = max(float3(dot(m0, nits), dot(m1, nits), dot(m2, nits)), float3(0.0));
            if (tone.x > 0.5) {
                float peak = max(nits.r, max(nits.g, nits.b));
                if (peak > 0.0) {
                    nits *= bt2390(peak) / peak;
                }
            }
            float3 encoded = clamp(encode(output_, nits), 0.0, 1.0);
            return half4(half3(encoded * a), half(a));
        }
        """;

    private readonly SKRuntimeEffect _effect;
    private readonly SKColorFilter _filter;

    private SkiaColorTransform(in ColorTransformParameters parameters, SKRuntimeEffect effect, SKColorFilter filter)
    {
        Parameters = parameters;
        _effect = effect;
        _filter = filter;
    }

    internal ColorTransformParameters Parameters { get; }

    internal SKColorFilter Filter => _filter;

    internal static SkiaColorTransform Create(in ColorTransformParameters parameters)
    {
        var effect = SKRuntimeEffect.CreateColorFilter(Sksl, out var errors)
            ?? throw new InvalidOperationException($"colour transform effect failed to compile: {errors}");
        SkiaCensus.Track(effect);
        var m = parameters.Matrix;
        var uniforms = new SKRuntimeEffectUniforms(effect)
        {
            { "m0", new[] { (float)m[0], (float)m[1], (float)m[2] } },
            { "m1", new[] { (float)m[3], (float)m[4], (float)m[5] } },
            { "m2", new[] { (float)m[6], (float)m[7], (float)m[8] } },
            {
                "source",
                new[]
                {
                    (float)(int)parameters.Source.Kind, (float)parameters.Source.Gamma,
                    (float)parameters.Source.MaxLuminance, (float)parameters.Anchor,
                }
            },
            {
                "output_",
                new[]
                {
                    (float)(int)parameters.Output.Kind, (float)parameters.Output.Gamma,
                    (float)parameters.Output.MaxLuminance, 0f,
                }
            },
            {
                "tone",
                new[]
                {
                    parameters.MapTones ? 1f : 0f, (float)parameters.ToneSourceMax,
                    (float)parameters.ToneTargetMax, (float)parameters.ToneKnee,
                }
            },
        };
        var filter = effect.ToColorFilter(uniforms)
            ?? throw new InvalidOperationException("colour transform effect rejected its uniforms.");
        return new SkiaColorTransform(parameters, effect, SkiaCensus.Track(filter));
    }

    public void Dispose()
    {
        SkiaCensus.Release(_filter);
        SkiaCensus.Release(_effect);
    }
}
