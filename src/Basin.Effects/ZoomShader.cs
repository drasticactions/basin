namespace Basin.Effects;

public static class ZoomShader
{
    public static readonly PixelShaderUniform[] Uniforms =
    [
        new("zoom", PixelShaderUniformType.Float),
        new("grid", PixelShaderUniformType.Float),
        new("translation", PixelShaderUniformType.Float2),
    ];

    private static byte[]? _spirv;

    public static PixelShaderSource Source => new()
    {
        SamplesTexture = true,
        Glsl = Glsl,
        Sksl = Sksl,
        SpirV = _spirv ??= LoadSpirV(),
    };

    private const string Glsl = """
        vec4 basin_pixel(vec2 coord) {
        vec2 srcPos = (coord - translation) / max(zoom, 0.0001);
        if (grid > 0.5) {
            vec2 center = floor(srcPos) + vec2(0.5);
            vec2 away = abs(srcPos - center);
            float edge = smoothstep(0.4, 0.5, max(away.x, away.y));
            return mix(basin_texture(center), vec4(0.0, 0.0, 0.0, 1.0), edge);
        }
        vec2 texel = srcPos - vec2(0.5);
        vec2 base = floor(texel);
        vec2 part = texel - base;
        vec2 sharp = clamp(((part - vec2(0.5)) * max(zoom, 1.0)) + vec2(0.5), 0.0, 1.0);
        return basin_texture(base + vec2(0.5) + sharp);
        }
        """;

    private const string Sksl = """
        half4 basin_pixel(float2 coord) {
        float2 srcPos = (coord - translation) / max(zoom, 0.0001);
        if (grid > 0.5) {
            float2 center = floor(srcPos) + float2(0.5);
            float2 away = abs(srcPos - center);
            float edge = smoothstep(0.4, 0.5, max(away.x, away.y));
            return mix(basin_texture(center), half4(0.0, 0.0, 0.0, 1.0), half(edge));
        }
        float2 texel = srcPos - float2(0.5);
        float2 base = floor(texel);
        float2 part = texel - base;
        float2 sharp = clamp(((part - float2(0.5)) * max(zoom, 1.0)) + float2(0.5), 0.0, 1.0);
        return basin_texture(base + float2(0.5) + sharp);
        }
        """;

    private static byte[] LoadSpirV()
    {
        using var stream = typeof(ZoomShader).Assembly.GetManifestResourceStream("zoom.frag.spv")
            ?? throw new InvalidOperationException("missing shader resource zoom.frag.spv");
        var code = new byte[stream.Length];
        stream.ReadExactly(code);
        return code;
    }
}
