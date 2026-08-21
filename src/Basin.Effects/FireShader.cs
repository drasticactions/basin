namespace Basin.Effects;

public static class FireShader
{
    public static readonly PixelShaderUniform[] Uniforms =
    [
        new("time", PixelShaderUniformType.Float),
        new("reveal", PixelShaderUniformType.Float),
        new("seed", PixelShaderUniformType.Float),
        new("rect", PixelShaderUniformType.Float4),
        new("tint", PixelShaderUniformType.Float3),
    ];

    private const string Body = """
        float basin_hash(vec2 p) {
            return fract(sin(dot(p, vec2(127.1, 311.7)) + seed) * 43758.5453);
        }
        float basin_noise(vec2 p) {
            vec2 i = floor(p);
            vec2 f = fract(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = basin_hash(i);
            float b = basin_hash(i + vec2(1.0, 0.0));
            float c = basin_hash(i + vec2(0.0, 1.0));
            float d = basin_hash(i + vec2(1.0, 1.0));
            return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
        }
        float basin_fbm(vec2 p) {
            float v = 0.0;
            float amp = 0.5;
            for (int i = 0; i < 4; i++) {
                v += amp * basin_noise(p);
                p *= 2.03;
                amp *= 0.5;
            }
            return v;
        }
        vec4 basin_pixel(vec2 coord) {
            vec2 uv = coord / u_size;
            float line = rect.y + rect.w * reveal;
            float below = uv.y - line;
            float n = basin_fbm(vec2(uv.x * 8.0, (uv.y * 8.0) - (time * 2.0)));
            float inX = smoothstep(rect.x - 0.06, rect.x, uv.x) * (1.0 - smoothstep(rect.x + rect.z, rect.x + rect.z + 0.06, uv.x));
            float band = 1.0 - smoothstep(0.0, 0.16 + (0.12 * n), abs(below));
            float intensity = band * band * (0.55 + (0.45 * n)) * inX;
            vec3 rgb = (tint * intensity * 1.6) + (vec3(1.0, 0.75, 0.3) * intensity * intensity);
            float alpha = clamp(intensity * 1.2, 0.0, 1.0);
            float scorch = 0.45 * (1.0 - smoothstep(0.0, 0.1, -below)) * step(below, 0.0) * inX;
            return vec4(clamp(rgb, 0.0, 1.0) * alpha, min(alpha + (scorch * (1.0 - alpha)), 1.0));
        }
        """;

    private static byte[]? _spirv;

    public static PixelShaderSource Source => new()
    {
        Glsl = Body,
        Sksl = SkslBody,
        SpirV = _spirv ??= LoadSpirV(),
    };

    private const string SkslBody = """
        float basin_hash(float2 p) {
            return fract(sin(dot(p, float2(127.1, 311.7)) + seed) * 43758.5453);
        }
        float basin_noise(float2 p) {
            float2 i = floor(p);
            float2 f = fract(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = basin_hash(i);
            float b = basin_hash(i + float2(1.0, 0.0));
            float c = basin_hash(i + float2(0.0, 1.0));
            float d = basin_hash(i + float2(1.0, 1.0));
            return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
        }
        float basin_fbm(float2 p) {
            float v = 0.0;
            float amp = 0.5;
            for (int i = 0; i < 4; i++) {
                v += amp * basin_noise(p);
                p *= 2.03;
                amp *= 0.5;
            }
            return v;
        }
        half4 basin_pixel(float2 coord) {
            float2 uv = coord / u_size;
            float line = rect.y + rect.w * reveal;
            float below = uv.y - line;
            float n = basin_fbm(float2(uv.x * 8.0, (uv.y * 8.0) - (time * 2.0)));
            float inX = smoothstep(rect.x - 0.06, rect.x, uv.x) * (1.0 - smoothstep(rect.x + rect.z, rect.x + rect.z + 0.06, uv.x));
            float band = 1.0 - smoothstep(0.0, 0.16 + (0.12 * n), abs(below));
            float intensity = band * band * (0.55 + (0.45 * n)) * inX;
            float3 rgb = (tint * intensity * 1.6) + (float3(1.0, 0.75, 0.3) * intensity * intensity);
            float alpha = clamp(intensity * 1.2, 0.0, 1.0);
            float scorch = 0.45 * (1.0 - smoothstep(0.0, 0.1, -below)) * step(below, 0.0) * inX;
            return half4(half3(clamp(rgb, 0.0, 1.0) * alpha), half(min(alpha + (scorch * (1.0 - alpha)), 1.0)));
        }
        """;

    private static byte[] LoadSpirV()
    {
        using var stream = typeof(FireShader).Assembly.GetManifestResourceStream("fire.frag.spv")
            ?? throw new InvalidOperationException("missing shader resource fire.frag.spv");
        var code = new byte[stream.Length];
        stream.ReadExactly(code);
        return code;
    }
}
