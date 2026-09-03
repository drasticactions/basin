namespace Basin.Effects;

public static class CtmShader
{
    public static readonly PixelShaderUniform[] Uniforms =
    [
        new("m0", PixelShaderUniformType.Float),
        new("m1", PixelShaderUniformType.Float),
        new("m2", PixelShaderUniformType.Float),
        new("m3", PixelShaderUniformType.Float),
        new("m4", PixelShaderUniformType.Float),
        new("m5", PixelShaderUniformType.Float),
        new("m6", PixelShaderUniformType.Float),
        new("m7", PixelShaderUniformType.Float),
        new("m8", PixelShaderUniformType.Float),
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
        vec3 basin_srgb_to_linear(vec3 c) {
            return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
        }
        vec3 basin_linear_to_srgb(vec3 c) {
            return mix(c * 12.92, (1.055 * pow(c, vec3(1.0 / 2.4))) - 0.055, step(0.0031308, c));
        }
        vec4 basin_pixel(vec2 coord) {
            vec4 c = basin_texture(coord);
            vec3 straight = c.a > 0.001 ? c.rgb / c.a : c.rgb;
            straight = clamp(straight, 0.0, 1.0);
            vec3 linear = basin_srgb_to_linear(straight);
            vec3 mapped = vec3(
                dot(vec3(m0, m1, m2), linear),
                dot(vec3(m3, m4, m5), linear),
                dot(vec3(m6, m7, m8), linear));
            vec3 encoded = basin_linear_to_srgb(clamp(mapped, 0.0, 1.0));
            return vec4(encoded * c.a, c.a);
        }
        """;

    private const string Sksl = """
        float3 basin_srgb_to_linear(float3 c) {
            return mix(c / 12.92, pow((c + 0.055) / 1.055, float3(2.4)), step(float3(0.04045), c));
        }
        float3 basin_linear_to_srgb(float3 c) {
            return mix(c * 12.92, (1.055 * pow(c, float3(1.0 / 2.4))) - 0.055, step(float3(0.0031308), c));
        }
        half4 basin_pixel(float2 coord) {
            half4 c = basin_texture(coord);
            float3 rgb = float3(c.rgb);
            float alpha = float(c.a);
            float3 straight = alpha > 0.001 ? rgb / alpha : rgb;
            straight = clamp(straight, 0.0, 1.0);
            float3 linear = basin_srgb_to_linear(straight);
            float3 mapped = float3(
                dot(float3(m0, m1, m2), linear),
                dot(float3(m3, m4, m5), linear),
                dot(float3(m6, m7, m8), linear));
            float3 encoded = basin_linear_to_srgb(clamp(mapped, 0.0, 1.0));
            return half4(half3(encoded * alpha), c.a);
        }
        """;

    private static byte[] LoadSpirV()
    {
        using var stream = typeof(CtmShader).Assembly.GetManifestResourceStream("ctm.frag.spv")
            ?? throw new InvalidOperationException("missing shader resource ctm.frag.spv");
        var code = new byte[stream.Length];
        stream.ReadExactly(code);
        return code;
    }
}
