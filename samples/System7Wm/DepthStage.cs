using Basin;
using Basin.Scene;
using System7.Avalonia.Rendering;

namespace System7Wm;

internal sealed class DepthStage : IPostStage, IDisposable
{
    public static readonly PixelShaderUniform[] Uniforms =
    [
        new("depth", PixelShaderUniformType.Float),
        new("dither", PixelShaderUniformType.Float),
    ];

    private const string Helpers = """
        float s7_bayer(vec2 p) {
            vec2 lo = mod(floor(p), 2.0);
            vec2 hi = mod(floor(p * 0.5), 2.0);
            float a = mod(2.0 * lo.x + 3.0 * lo.y, 4.0);
            float b = mod(2.0 * hi.x + 3.0 * hi.y, 4.0);
            return (4.0 * a + b + 0.5) / 16.0;
        }

        vec4 s7_pick(vec4 best, vec3 c, vec3 candidate) {
            vec3 d = c - candidate;
            float e = dot(d, d);
            return e < best.a ? vec4(candidate, e) : best;
        }

        vec3 s7_quantize(vec3 c, vec2 p) {
            float t = dither > 0.5 ? s7_bayer(p) : 0.5;
            float l = dot(c, vec3(0.299, 0.587, 0.114));
            if (depth < 1.5) {
                return vec3(l >= t ? 1.0 : 0.0);
            }
            if (depth < 2.5) {
                float k = clamp(floor(l * 3.0 + t), 0.0, 3.0);
                return vec3(k < 0.5 ? 0.0 : k < 1.5 ? 85.0 / 255.0 : k < 2.5 ? 172.0 / 255.0 : 1.0);
            }
            if (depth < 4.5) {
                vec3 x = clamp(c + (t - 0.5) * 0.3, 0.0, 1.0);
                vec4 b = vec4(1.0, 1.0, 1.0, 1000.0);
                b = s7_pick(b, x, vec3(1.0, 1.0, 1.0));
                b = s7_pick(b, x, vec3(252.0, 243.0, 5.0) / 255.0);
                b = s7_pick(b, x, vec3(255.0, 100.0, 2.0) / 255.0);
                b = s7_pick(b, x, vec3(221.0, 8.0, 6.0) / 255.0);
                b = s7_pick(b, x, vec3(242.0, 8.0, 132.0) / 255.0);
                b = s7_pick(b, x, vec3(70.0, 0.0, 165.0) / 255.0);
                b = s7_pick(b, x, vec3(0.0, 0.0, 212.0) / 255.0);
                b = s7_pick(b, x, vec3(2.0, 171.0, 234.0) / 255.0);
                b = s7_pick(b, x, vec3(31.0, 183.0, 20.0) / 255.0);
                b = s7_pick(b, x, vec3(0.0, 100.0, 17.0) / 255.0);
                b = s7_pick(b, x, vec3(86.0, 44.0, 5.0) / 255.0);
                b = s7_pick(b, x, vec3(144.0, 113.0, 58.0) / 255.0);
                b = s7_pick(b, x, vec3(192.0, 192.0, 192.0) / 255.0);
                b = s7_pick(b, x, vec3(128.0, 128.0, 128.0) / 255.0);
                b = s7_pick(b, x, vec3(32.0, 32.0, 32.0) / 255.0);
                b = s7_pick(b, x, vec3(0.0, 0.0, 0.0));
                return b.rgb;
            }
            if (depth < 8.5) {
                vec3 x = clamp(c + (t - 0.5) * 0.2, 0.0, 1.0);
                vec3 cube = floor(x * 5.0 + 0.5) / 5.0;
                vec3 d = x - cube;
                vec4 b = vec4(cube, dot(d, d));
                b = s7_pick(b, x, vec3(floor(dot(x, vec3(1.0 / 3.0)) * 15.0 + 0.5) / 15.0));
                b = s7_pick(b, x, vec3(floor(x.r * 15.0 + 0.5) / 15.0, 0.0, 0.0));
                b = s7_pick(b, x, vec3(0.0, floor(x.g * 15.0 + 0.5) / 15.0, 0.0));
                b = s7_pick(b, x, vec3(0.0, 0.0, floor(x.b * 15.0 + 0.5) / 15.0));
                return b.rgb;
            }
            if (depth < 16.5) {
                return floor(c * 31.0 + 0.5) / 31.0;
            }
            return c;
        }
        """;

    private const string GlslMain = """
        vec4 basin_pixel(vec2 coord) {
            vec4 c = basin_texture(coord);
            vec3 straight = c.a > 0.001 ? c.rgb / c.a : c.rgb;
            straight = clamp(straight, 0.0, 1.0);
            return vec4(s7_quantize(straight, floor(coord)) * c.a, c.a);
        }
        """;

    private const string SkslMain = """
        half4 basin_pixel(float2 coord) {
            half4 c = basin_texture(coord);
            float alpha = float(c.a);
            float3 straight = alpha > 0.001 ? float3(c.rgb) / alpha : float3(c.rgb);
            straight = clamp(straight, 0.0, 1.0);
            return half4(half3(s7_quantize(straight, floor(coord)) * alpha), c.a);
        }
        """;

    private static byte[]? _spirv;

    private readonly IPixelShader? _shader;
    private readonly PixelShaderUniformValue[] _values = new PixelShaderUniformValue[2];

    public DepthStage(IRenderer renderer)
    {
        _shader = renderer.CompilePixelShader(Source, Uniforms);
    }

    public bool IsSupported => _shader is not null;

    public System7ColorDepth Depth { get; private set; } = System7ColorDepth.Rgb888;

    public bool Dither { get; private set; } = true;

    public static PixelShaderSource Source => new()
    {
        SamplesTexture = true,
        Glsl = Helpers + "\n" + GlslMain,
        Sksl = Helpers.Replace("vec", "float", StringComparison.Ordinal) + "\n" + SkslMain,
        SpirV = _spirv ??= LoadSpirV(),
    };

    public void Set(System7ColorDepth depth, bool dither)
    {
        Depth = depth;
        Dither = dither;
        _values[0] = (float)(int)depth;
        _values[1] = dither ? 1f : 0f;
        _shader?.SetUniforms(_values);
    }

    public void Render(IRenderPass pass, ITexture frame, in PostContext context)
    {
        pass.AddTexture(frame, new TextureRenderOptions
        {
            DstBox = new Box(0, 0, context.Width, context.Height),
            Shader = Depth == System7ColorDepth.Rgb888 ? null : _shader,
        });
    }

    public void Dispose() => _shader?.Dispose();

    private static byte[] LoadSpirV()
    {
        using var stream = typeof(DepthStage).Assembly.GetManifestResourceStream("depth.frag.spv")
            ?? throw new InvalidOperationException("The shader resource depth.frag.spv is not embedded in the program.");
        var code = new byte[stream.Length];
        stream.ReadExactly(code);
        return code;
    }
}
