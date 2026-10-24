using Basin;

namespace LunaWm;

internal static class CardShader
{
    public const double Radius = 24;

    public static readonly PixelShaderUniform[] Uniforms =
    [
        new("radius", PixelShaderUniformType.Float),
        new("brightness", PixelShaderUniformType.Float),
    ];

    private static byte[]? _spirv;

    public static PixelShaderSource Source => new()
    {
        SamplesTexture = true,
        Glsl = """
            vec4 basin_pixel(vec2 coord) {
                vec4 c = basin_texture(coord);
                vec2 halfSize = u_size * 0.5;
                vec2 p = abs(coord - halfSize) - (halfSize - vec2(radius));
                float d = length(max(p, vec2(0.0))) - radius;
                float mask = 1.0 - smoothstep(-1.0, 0.0, d);
                return vec4(c.rgb * brightness, c.a) * mask;
            }
            """,
        Sksl = """
            half4 basin_pixel(float2 coord) {
                half4 c = basin_texture(coord);
                float2 halfSize = u_size * 0.5;
                float2 p = abs(coord - halfSize) - (halfSize - float2(radius));
                float d = length(max(p, float2(0.0))) - radius;
                float mask = 1.0 - smoothstep(-1.0, 0.0, d);
                return half4(c.rgb * half(brightness), c.a) * half(mask);
            }
            """,
        SpirV = _spirv ??= LoadSpirV(),
    };

    private static byte[] LoadSpirV()
    {
        using var stream = typeof(CardShader).Assembly.GetManifestResourceStream("card.frag.spv")
            ?? throw new InvalidOperationException("The shader resource card.frag.spv is not embedded in the program.");
        var code = new byte[stream.Length];
        stream.ReadExactly(code);
        return code;
    }
}
