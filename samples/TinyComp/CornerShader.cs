using Basin;

namespace TinyComp;

internal static class CornerShader
{
    public static readonly PixelShaderUniform[] Uniforms =
    [
        new("radius", PixelShaderUniformType.Float),
    ];

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
                return c * mask;
            }
            """,
        Sksl = """
            half4 basin_pixel(float2 coord) {
                half4 c = basin_texture(coord);
                float2 halfSize = u_size * 0.5;
                float2 p = abs(coord - halfSize) - (halfSize - float2(radius));
                float d = length(max(p, float2(0.0))) - radius;
                float mask = 1.0 - smoothstep(-1.0, 0.0, d);
                return c * half(mask);
            }
            """,
        SpirV = _spirv ??= LoadSpirV(),
    };

    private static byte[]? _spirv;

    public static readonly PixelShaderUniform[] OuterUniforms =
    [
        new("radius", PixelShaderUniformType.Float),
        new("outer", PixelShaderUniformType.Float2),
        new("offset", PixelShaderUniformType.Float2),
    ];

    public static PixelShaderSource OuterSource => new()
    {
        SamplesTexture = true,
        Glsl = """
            vec4 basin_pixel(vec2 coord) {
                vec4 c = basin_texture(coord);
                vec2 p0 = coord + offset;
                vec2 halfSize = outer * 0.5;
                vec2 p = abs(p0 - halfSize) - (halfSize - vec2(radius));
                float d = length(max(p, vec2(0.0))) - radius;
                float mask = 1.0 - smoothstep(-1.0, 0.0, d);
                return c * mask;
            }
            """,
        Sksl = """
            half4 basin_pixel(float2 coord) {
                half4 c = basin_texture(coord);
                float2 p0 = coord + offset;
                float2 halfSize = outer * 0.5;
                float2 p = abs(p0 - halfSize) - (halfSize - float2(radius));
                float d = length(max(p, float2(0.0))) - radius;
                float mask = 1.0 - smoothstep(-1.0, 0.0, d);
                return c * half(mask);
            }
            """,
        SpirV = _outerSpirv ??= LoadSpirV("corners_outer.frag.spv"),
    };

    private static byte[]? _outerSpirv;

    private static byte[] LoadSpirV(string name = "corners.frag.spv")
    {
        using var stream = typeof(CornerShader).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"missing shader resource {name}");
        var code = new byte[stream.Length];
        stream.ReadExactly(code);
        return code;
    }
}
