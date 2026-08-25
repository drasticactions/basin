namespace Basin.Effects;

public static class InvertShader
{
    public static readonly PixelShaderUniform[] Uniforms = [];

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
            vec3 gamma = vec3(1.0) - pow(max(linear, vec3(0.0)), vec3(1.0 / 2.2));
            linear = pow(max(gamma, vec3(0.0)), vec3(2.2));
            return vec4(basin_linear_to_srgb(linear) * c.a, c.a);
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
            float3 straight = c.a > 0.001 ? float3(c.rgb) / float(c.a) : float3(c.rgb);
            straight = clamp(straight, 0.0, 1.0);
            float3 linear = basin_srgb_to_linear(straight);
            float3 gamma = float3(1.0) - pow(max(linear, float3(0.0)), float3(1.0 / 2.2));
            linear = pow(max(gamma, float3(0.0)), float3(2.2));
            return half4(half3(basin_linear_to_srgb(linear) * float(c.a)), c.a);
        }
        """;

    private static byte[] LoadSpirV()
    {
        using var stream = typeof(InvertShader).Assembly.GetManifestResourceStream("invert.frag.spv")
            ?? throw new InvalidOperationException("missing shader resource invert.frag.spv");
        var code = new byte[stream.Length];
        stream.ReadExactly(code);
        return code;
    }
}
