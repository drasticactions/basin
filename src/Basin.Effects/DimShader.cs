namespace Basin.Effects;

/// <summary>
/// The pixel shader that <see cref="DimInactiveEffect"/> drives. It desaturates and darkens a texture by one factor.
/// </summary>
public static class DimShader
{
    /// <summary>
    /// The uniforms of the dim shader. The one uniform <c>dim</c> is the brightness factor, in the order that <see cref="IPixelShader.SetUniforms"/> takes them.
    /// </summary>
    public static readonly PixelShaderUniform[] Uniforms =
    [
        new("dim", PixelShaderUniformType.Float),
    ];

    private static byte[]? _spirv;

    /// <summary>
    /// Gets the GLSL, SkSL and SPIR-V dialects of the dim shader. The one uniform <c>dim</c> is the brightness factor. The first read loads the SPIR-V from an embedded resource.
    /// </summary>
    public static PixelShaderSource Source => new()
    {
        SamplesTexture = true,
        Glsl = Glsl,
        Sksl = Sksl,
        SpirV = _spirv ??= LoadSpirV(),
    };

    private const string Glsl = """
        vec4 basin_pixel(vec2 coord) {
            vec4 c = basin_texture(coord);
            vec3 straight = c.a > 0.001 ? c.rgb / c.a : c.rgb;
            float luma = dot(straight, vec3(0.2126, 0.7152, 0.0722));
            vec3 dimmed = mix(vec3(luma), straight, dim) * dim;
            return vec4(dimmed * c.a, c.a);
        }
        """;

    private const string Sksl = """
        half4 basin_pixel(float2 coord) {
            half4 c = basin_texture(coord);
            float alpha = float(c.a);
            float3 straight = alpha > 0.001 ? float3(c.rgb) / alpha : float3(c.rgb);
            float luma = dot(straight, float3(0.2126, 0.7152, 0.0722));
            float3 dimmed = mix(float3(luma), straight, dim) * dim;
            return half4(half3(dimmed * alpha), c.a);
        }
        """;

    private static byte[] LoadSpirV()
    {
        using var stream = typeof(DimShader).Assembly.GetManifestResourceStream("dim.frag.spv")
            ?? throw new InvalidOperationException("missing shader resource dim.frag.spv");
        var code = new byte[stream.Length];
        stream.ReadExactly(code);
        return code;
    }
}
