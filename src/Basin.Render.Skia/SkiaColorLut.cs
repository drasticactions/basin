using SkiaSharp;

namespace Basin.Render.Skia;

internal sealed class SkiaColorLut : IColorLut
{
    private const string Sksl = """
        uniform shader atlas;
        uniform float n;
        half4 main(half4 c) {
            half a = c.a;
            half3 s = a > 0.0 ? clamp(c.rgb / a, 0.0, 1.0) : half3(0.0);
            float3 v = float3(s) * (n - 1.0);
            float b0 = floor(v.z);
            float b1 = min(b0 + 1.0, n - 1.0);
            float2 uv = float2(v.x + 0.5, v.y + 0.5);
            half3 c0 = atlas.eval(float2(b0 * n + uv.x, uv.y)).rgb;
            half3 c1 = atlas.eval(float2(b1 * n + uv.x, uv.y)).rgb;
            half3 t = mix(c0, c1, half(v.z - b0));
            return half4(t * a, a);
        }
        """;

    private readonly SKImage _atlas;
    private readonly SKShader _atlasShader;
    private readonly SKRuntimeEffect _effect;
    private readonly SKColorFilter _filter;

    private SkiaColorLut(SKImage atlas, SKShader atlasShader, SKRuntimeEffect effect, SKColorFilter filter)
    {
        _atlas = atlas;
        _atlasShader = atlasShader;
        _effect = effect;
        _filter = filter;
    }

    internal SKColorFilter Filter => _filter;

    internal static SkiaColorLut Create(
        ColorLut3D lut,
        SKGraphiteRecorder? recorder = null)
    {
        var n = lut.Size;

        var pixels = new byte[n * n * n * 8];
        var one = BitConverter.HalfToUInt16Bits((Half)1f);
        for (var b = 0; b < n; b++)
        {
            for (var g = 0; g < n; g++)
            {
                for (var r = 0; r < n; r++)
                {
                    var src = ((b * n + g) * n + r) * 3;
                    var dst = (g * n * n + b * n + r) * 8;
                    Write(pixels, dst, BitConverter.HalfToUInt16Bits((Half)lut.Data[src]));
                    Write(pixels, dst + 2, BitConverter.HalfToUInt16Bits((Half)lut.Data[src + 1]));
                    Write(pixels, dst + 4, BitConverter.HalfToUInt16Bits((Half)lut.Data[src + 2]));
                    Write(pixels, dst + 6, one);
                }
            }
        }

        var info = new SKImageInfo(n * n, n, SKColorType.RgbaF16, SKAlphaType.Opaque);
        var atlas = SKImage.FromPixelCopy(info, pixels, n * n * 8)
            ?? throw new InvalidOperationException("Skia rejected the LUT atlas image.");
        SkiaCensus.Track(atlas);
        if (recorder is not null)
        {
            var uploaded = atlas.ToTextureImage(recorder)
                ?? throw new InvalidOperationException("Graphite could not upload the LUT atlas.");
            SkiaCensus.Release(atlas);
            atlas = SkiaCensus.Track(uploaded);
        }

        var atlasShader = SkiaCensus.Track(atlas.ToShader(
            SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Linear)));
        var effect = SKRuntimeEffect.CreateColorFilter(Sksl, out var errors)
            ?? throw new InvalidOperationException($"LUT effect failed to compile: {errors}");
        SkiaCensus.Track(effect);

        var uniforms = new SKRuntimeEffectUniforms(effect) { { "n", (float)n } };
        var children = new SKRuntimeEffectChildren(effect) { { "atlas", atlasShader } };
        var filter = effect.ToColorFilter(uniforms, children)
            ?? throw new InvalidOperationException("LUT effect rejected its uniforms or children.");
        return new SkiaColorLut(atlas, atlasShader, effect, SkiaCensus.Track(filter));
    }

    public void Dispose()
    {
        SkiaCensus.Release(_filter);
        SkiaCensus.Release(_effect);
        SkiaCensus.Release(_atlasShader);
        SkiaCensus.Release(_atlas);
    }

    private static void Write(byte[] pixels, int offset, ushort bits)
    {
        pixels[offset] = (byte)bits;
        pixels[offset + 1] = (byte)(bits >> 8);
    }
}
