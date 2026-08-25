namespace Basin.Effects;

public static class ColorBlindnessShader
{
    public static readonly PixelShaderUniform[] Uniforms =
    [
        new("mode", PixelShaderUniformType.Float),
        new("intensity", PixelShaderUniformType.Float),
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
        if (mode >= 2.5) {
            float luma = dot(linear, vec3(0.2126, 0.7152, 0.0722));
            linear = mix(linear, vec3(luma), clamp(intensity, 0.0, 1.0));
        } else {
            mat3 srgbToLMS = mat3(
                17.8824, 3.45565, 0.0299566,
                43.5161, 27.1554, 0.184309,
                4.11935, 3.86714, 1.46709);
            mat3 errorMat = mat3(
                0.0809444479, -0.0102485335, -0.000365296938,
                -0.130504409, 0.0540193266, -0.00412161469,
                0.116721066, -0.113614708, 0.693511405);
            mat3 defect = mat3(0.0, 0.0, 0.0, 2.02344, 1.0, 0.0, -2.52581, 0.0, 1.0);
            if (mode >= 1.5) {
                defect = mat3(1.0, 0.0, -0.395913, 0.0, 1.0, 0.801109, 0.0, 0.0, 0.0);
            } else if (mode >= 0.5) {
                defect = mat3(1.0, 0.494207, 0.0, 0.0, 0.0, 0.0, 0.0, 1.24827, 1.0);
            }
            vec3 lms = defect * (srgbToLMS * linear);
            vec3 err = errorMat * lms;
            vec3 diff = (linear - err) * intensity;
            vec3 correction = vec3(0.0, (diff.r * 0.7) + diff.g, (diff.r * 0.7) + diff.b);
            linear = linear + correction;
        }
            vec3 encoded = basin_linear_to_srgb(clamp(linear, 0.0, 1.0));
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
        if (mode >= 2.5) {
            float luma = dot(linear, float3(0.2126, 0.7152, 0.0722));
            linear = mix(linear, float3(luma), clamp(intensity, 0.0, 1.0));
        } else {
            float3x3 srgbToLMS = float3x3(
                17.8824, 3.45565, 0.0299566,
                43.5161, 27.1554, 0.184309,
                4.11935, 3.86714, 1.46709);
            float3x3 errorMat = float3x3(
                0.0809444479, -0.0102485335, -0.000365296938,
                -0.130504409, 0.0540193266, -0.00412161469,
                0.116721066, -0.113614708, 0.693511405);
            float3x3 defect = float3x3(0.0, 0.0, 0.0, 2.02344, 1.0, 0.0, -2.52581, 0.0, 1.0);
            if (mode >= 1.5) {
                defect = float3x3(1.0, 0.0, -0.395913, 0.0, 1.0, 0.801109, 0.0, 0.0, 0.0);
            } else if (mode >= 0.5) {
                defect = float3x3(1.0, 0.494207, 0.0, 0.0, 0.0, 0.0, 0.0, 1.24827, 1.0);
            }
            float3 lms = defect * (srgbToLMS * linear);
            float3 err = errorMat * lms;
            float3 diff = (linear - err) * intensity;
            float3 correction = float3(0.0, (diff.r * 0.7) + diff.g, (diff.r * 0.7) + diff.b);
            linear = linear + correction;
        }
            float3 encoded = basin_linear_to_srgb(clamp(linear, 0.0, 1.0));
            return half4(half3(encoded * alpha), c.a);
        }
        """;

    private static byte[] LoadSpirV()
    {
        using var stream = typeof(ColorBlindnessShader).Assembly.GetManifestResourceStream("colorblind.frag.spv")
            ?? throw new InvalidOperationException("missing shader resource colorblind.frag.spv");
        var code = new byte[stream.Length];
        stream.ReadExactly(code);
        return code;
    }
}
