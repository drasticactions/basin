using System.Runtime.InteropServices;
using System.Text;
using Basin.Diagnostics;
using SkiaSharp;

namespace Basin.Render.Skia;

internal sealed class SkiaPixelShader : IPixelShader
{
    private readonly SKRuntimeEffect _effect;
    private readonly PixelShaderUniformType[] _types;
    private readonly int[] _offsets;
    private readonly int _sizeOffset;
    private readonly int _srcOffset;
    private readonly byte[] _block;
    private readonly SharedEffect _shared;
    private nint _shader;
    private float _width = -1f;
    private float _height = -1f;
    private bool _dirty = true;
    private bool _disposed;

    internal readonly bool SamplesTexture;

    private SkiaPixelShader(SKRuntimeEffect effect, ReadOnlySpan<PixelShaderUniform> uniforms, bool samplesTexture)
    {
        _effect = effect;
        SamplesTexture = samplesTexture;
        _types = new PixelShaderUniformType[uniforms.Length];
        _offsets = new int[uniforms.Length];
        _block = new byte[effect.UniformSize];
        _sizeOffset = OffsetOf(effect, "u_size");
        _srcOffset = samplesTexture ? OffsetOf(effect, "u_src") : -1;
        for (var i = 0; i < uniforms.Length; i++)
        {
            _types[i] = uniforms[i].Type;
            _offsets[i] = OffsetOf(effect, uniforms[i].Name);
        }

        _shared = new SharedEffect();
    }

    private SkiaPixelShader(SkiaPixelShader source)
    {
        _effect = source._effect;
        SamplesTexture = source.SamplesTexture;
        _types = source._types;
        _offsets = source._offsets;
        _sizeOffset = source._sizeOffset;
        _srcOffset = source._srcOffset;
        _block = (byte[])source._block.Clone();
        _shared = source._shared;
        _shared.References++;
    }

    internal static SkiaPixelShader Create(in PixelShaderSource source, ReadOnlySpan<PixelShaderUniform> uniforms)
    {
        var sksl = BuildSksl(source.Sksl!, uniforms, source.SamplesTexture);
        var effect = SKRuntimeEffect.CreateShader(sksl, out var errors)
            ?? throw new InvalidOperationException($"The pixel shader did not compile: {errors}");
        SkiaCensus.Track(effect);
        return new SkiaPixelShader(effect, uniforms, source.SamplesTexture);
    }

    public IPixelShader? CreateInstance()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new SkiaPixelShader(this);
    }

    public void SetUniforms(ReadOnlySpan<PixelShaderUniformValue> values)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (values.Length != _types.Length)
        {
            throw new ArgumentException("The value count must match the declared uniform count.", nameof(values));
        }

        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            var at = _block.AsSpan(_offsets[i]);
            switch (_types[i])
            {
                case PixelShaderUniformType.Float:
                    Write(at, value.X);
                    break;
                case PixelShaderUniformType.Float2:
                    Write(at, value.X, value.Y);
                    break;
                case PixelShaderUniformType.Float3:
                    Write(at, value.X, value.Y);
                    Write(at[8..], value.Z);
                    break;
                case PixelShaderUniformType.Float4:
                    Write(at, value.X, value.Y);
                    Write(at[8..], value.Z, value.W);
                    break;
            }
        }

        _dirty = true;
    }

    internal nint Realize(float width, float height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_shader != 0 && !_dirty && _width == width && _height == height)
        {
            return _shader;
        }

        var built = Build(width, height, 0, default);
        if (_shader != 0)
        {
            SkiaShaderNative.UnrefShader(_shader);
            BasinCounters.Untrack();
        }

        _shader = built;
        BasinCounters.Track();
        _width = width;
        _height = height;
        _dirty = false;
        return _shader;
    }

    internal nint RealizeWithChild(float width, float height, nint child, in FBox src)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Build(width, height, child, src);
    }

    private unsafe nint Build(float width, float height, nint child, in FBox src)
    {
        Write(_block.AsSpan(_sizeOffset), width, height);
        if (_srcOffset >= 0)
        {
            Write(_block.AsSpan(_srcOffset), (float)src.X, (float)src.Y);
            Write(_block.AsSpan(_srcOffset + 8), (float)src.Width, (float)src.Height);
        }

        nint shader;
        fixed (byte* block = _block)
        {
            var data = SkiaShaderNative.CopyData(block, _block.Length);
            try
            {
                shader = child == 0
                    ? SkiaShaderNative.MakeShader(_effect.Handle, data, null, 0, null)
                    : SkiaShaderNative.MakeShader(_effect.Handle, data, &child, 1, null);
            }
            finally
            {
                SkiaShaderNative.UnrefData(data);
            }
        }

        return shader != 0 ? shader : throw new InvalidOperationException("The pixel shader rejected its uniforms or children.");
    }

    private static void Write(Span<byte> at, float x) => MemoryMarshal.Write(at, in x);

    private static void Write(Span<byte> at, float x, float y)
    {
        MemoryMarshal.Write(at, in x);
        MemoryMarshal.Write(at[4..], in y);
    }

    private static unsafe int OffsetOf(SKRuntimeEffect effect, string name)
    {
        var names = effect.Uniforms;
        for (var i = 0; i < names.Count; i++)
        {
            if (names[i] == name)
            {
                SkiaShaderNative.Uniform uniform;
                SkiaShaderNative.GetUniform(effect.Handle, i, &uniform);
                return (int)uniform.Offset;
            }
        }

        throw new InvalidOperationException($"The pixel shader declares no uniform named {name}.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_shader != 0)
        {
            SkiaShaderNative.UnrefShader(_shader);
            BasinCounters.Untrack();
            _shader = 0;
        }

        if (--_shared.References == 0)
        {
            SkiaCensus.Release(_effect);
        }
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

    private sealed class SharedEffect
    {
        public int References { get; set; } = 1;
    }
}
