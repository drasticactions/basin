using System.Text;
using SkiaSharp;

namespace Basin.Render.Skia;

internal sealed class SkiaPixelShader : IPixelShader
{
    private readonly SKRuntimeEffect _effect;
    private readonly string[] _names;
    private readonly PixelShaderUniformType[] _types;
    private readonly PixelShaderUniformValue[] _values;
    private SKShader? _shader;
    private float _width = -1f;
    private float _height = -1f;
    private bool _dirty = true;
    private bool _disposed;

    internal readonly bool SamplesTexture;

    private SkiaPixelShader(SKRuntimeEffect effect, ReadOnlySpan<PixelShaderUniform> uniforms, bool samplesTexture)
    {
        _effect = effect;
        SamplesTexture = samplesTexture;
        _names = new string[uniforms.Length];
        _types = new PixelShaderUniformType[uniforms.Length];
        _values = new PixelShaderUniformValue[uniforms.Length];
        for (var i = 0; i < uniforms.Length; i++)
        {
            _names[i] = uniforms[i].Name;
            _types[i] = uniforms[i].Type;
        }
    }

    internal static SkiaPixelShader Create(in PixelShaderSource source, ReadOnlySpan<PixelShaderUniform> uniforms)
    {
        var sksl = BuildSksl(source.Sksl!, uniforms, source.SamplesTexture);
        var effect = SKRuntimeEffect.CreateShader(sksl, out var errors)
            ?? throw new InvalidOperationException($"pixel shader failed to compile: {errors}");
        SkiaCensus.Track(effect);
        return new SkiaPixelShader(effect, uniforms, source.SamplesTexture);
    }

    public void SetUniforms(ReadOnlySpan<PixelShaderUniformValue> values)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (values.Length != _values.Length)
        {
            throw new ArgumentException("value count must match the declared uniform count", nameof(values));
        }

        values.CopyTo(_values);
        _dirty = true;
    }

    internal SKShader Realize(float width, float height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_shader is not null && !_dirty && _width == width && _height == height)
        {
            return _shader;
        }

        SkiaCensus.Release(_shader);
        _shader = SkiaCensus.Track(Build(width, height, null, default));
        _width = width;
        _height = height;
        _dirty = false;
        return _shader;
    }

    internal SKShader RealizeWithChild(float width, float height, SKShader child, in FBox src)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Build(width, height, child, src);
    }

    private SKShader Build(float width, float height, SKShader? child, in FBox src)
    {
        var uniforms = new SKRuntimeEffectUniforms(_effect) { { "u_size", new[] { width, height } } };
        if (SamplesTexture)
        {
            uniforms.Add("u_src", new[] { (float)src.X, (float)src.Y, (float)src.Width, (float)src.Height });
        }

        for (var i = 0; i < _values.Length; i++)
        {
            var value = _values[i];
            switch (_types[i])
            {
                case PixelShaderUniformType.Float:
                    uniforms.Add(_names[i], value.X);
                    break;
                case PixelShaderUniformType.Float2:
                    uniforms.Add(_names[i], new[] { value.X, value.Y });
                    break;
                case PixelShaderUniformType.Float3:
                    uniforms.Add(_names[i], new[] { value.X, value.Y, value.Z });
                    break;
                case PixelShaderUniformType.Float4:
                    uniforms.Add(_names[i], new[] { value.X, value.Y, value.Z, value.W });
                    break;
            }
        }

        SKShader? shader;
        if (child is null)
        {
            shader = _effect.ToShader(uniforms);
        }
        else
        {
            var children = new SKRuntimeEffectChildren(_effect) { { "u_texture", child } };
            shader = _effect.ToShader(uniforms, children);
        }

        return shader ?? throw new InvalidOperationException("pixel shader rejected its uniforms or children");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SkiaCensus.Release(_shader);
        _shader = null;
        SkiaCensus.Release(_effect);
    }

    private static string BuildSksl(string source, ReadOnlySpan<PixelShaderUniform> uniforms, bool samplesTexture)
    {
        var builder = new StringBuilder();
        builder.AppendLine("uniform float2 u_size;");
        if (samplesTexture)
        {
            builder.AppendLine("uniform shader u_texture;");
            builder.AppendLine("uniform float4 u_src;");
        }

        foreach (var uniform in uniforms)
        {
            var type = uniform.Type switch
            {
                PixelShaderUniformType.Float => "float",
                PixelShaderUniformType.Float2 => "float2",
                PixelShaderUniformType.Float3 => "float3",
                PixelShaderUniformType.Float4 => "float4",
                _ => throw new ArgumentOutOfRangeException(nameof(uniforms)),
            };
            builder.Append("uniform ").Append(type).Append(' ').Append(uniform.Name).AppendLine(";");
        }

        if (samplesTexture)
        {
            builder.AppendLine("half4 basin_texture(float2 coord) {");
            builder.AppendLine("    return u_texture.eval(u_src.xy + (coord / u_size) * u_src.zw);");
            builder.AppendLine("}");
        }

        builder.AppendLine(source);
        builder.AppendLine("half4 main(float2 coord) { return basin_pixel(coord); }");
        return builder.ToString();
    }
}
