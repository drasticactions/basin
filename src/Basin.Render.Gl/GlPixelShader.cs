using System.Text;
using Basin.Diagnostics;
using Silk.NET.OpenGLES;

namespace Basin.Render.Gl;

internal sealed class GlPixelShader : IPixelShader
{
    private readonly GlRenderer _renderer;
    private readonly int[] _locations;
    private readonly int[] _lutLocations;
    private readonly PixelShaderUniformType[] _types;
    private readonly PixelShaderUniformValue[] _values;
    private bool _disposed;

    internal readonly ShaderProgram Program;
    internal readonly ShaderProgram? LutProgram;
    internal readonly bool SamplesTexture;

    internal GlPixelShader(GlRenderer renderer, ShaderProgram program, ShaderProgram? lutProgram, ReadOnlySpan<PixelShaderUniform> uniforms, bool samplesTexture)
    {
        _renderer = renderer;
        Program = program;
        LutProgram = lutProgram;
        SamplesTexture = samplesTexture;
        _locations = new int[uniforms.Length];
        _lutLocations = new int[uniforms.Length];
        _types = new PixelShaderUniformType[uniforms.Length];
        _values = new PixelShaderUniformValue[uniforms.Length];
        for (var i = 0; i < uniforms.Length; i++)
        {
            _locations[i] = renderer.Gl.GetUniformLocation(program.Program, uniforms[i].Name);
            _lutLocations[i] = lutProgram is null ? -1 : renderer.Gl.GetUniformLocation(lutProgram.Program, uniforms[i].Name);
            _types[i] = uniforms[i].Type;
        }

        BasinCounters.Track();
    }

    public void SetUniforms(ReadOnlySpan<PixelShaderUniformValue> values)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (values.Length != _values.Length)
        {
            throw new ArgumentException("value count must match the declared uniform count", nameof(values));
        }

        values.CopyTo(_values);
    }

    internal void WriteUniforms(GL gl, bool lut = false)
    {
        var locations = lut ? _lutLocations : _locations;
        for (var i = 0; i < _values.Length; i++)
        {
            var value = _values[i];
            switch (_types[i])
            {
                case PixelShaderUniformType.Float:
                    gl.Uniform1(locations[i], value.X);
                    break;
                case PixelShaderUniformType.Float2:
                    gl.Uniform2(locations[i], value.X, value.Y);
                    break;
                case PixelShaderUniformType.Float3:
                    gl.Uniform3(locations[i], value.X, value.Y, value.Z);
                    break;
                case PixelShaderUniformType.Float4:
                    gl.Uniform4(locations[i], value.X, value.Y, value.Z, value.W);
                    break;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        BasinCounters.Untrack();
        _renderer.ReleaseShader(Program);
        if (LutProgram is not null)
        {
            _renderer.ReleaseShader(LutProgram);
        }
    }

    internal static string BuildFragment(string source, ReadOnlySpan<PixelShaderUniform> uniforms, bool samplesTexture, bool lut = false)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#version 300 es");
        builder.AppendLine("precision highp float;");
        if (lut)
        {
            builder.AppendLine("precision highp sampler3D;");
        }

        builder.AppendLine("in vec2 v_uv;");
        builder.AppendLine("out vec4 color;");
        builder.AppendLine("uniform vec2 u_size;");
        builder.AppendLine("uniform float u_alpha;");
        if (samplesTexture)
        {
            builder.AppendLine("uniform sampler2D u_texture;");
            builder.AppendLine("uniform vec4 u_src;");
            builder.AppendLine("uniform float u_forceOpaque;");
        }

        if (lut)
        {
            builder.AppendLine("uniform sampler3D u_lut;");
        }

        foreach (var uniform in uniforms)
        {
            var type = uniform.Type switch
            {
                PixelShaderUniformType.Float => "float",
                PixelShaderUniformType.Float2 => "vec2",
                PixelShaderUniformType.Float3 => "vec3",
                PixelShaderUniformType.Float4 => "vec4",
                _ => throw new ArgumentOutOfRangeException(nameof(uniforms)),
            };
            builder.Append("uniform ").Append(type).Append(' ').Append(uniform.Name).AppendLine(";");
        }

        if (samplesTexture)
        {
            builder.AppendLine("vec4 basin_texture(vec2 coord) {");
            builder.AppendLine("    vec4 c = texture(u_texture, u_src.xy + (coord / u_size) * u_src.zw);");
            builder.AppendLine("    c.a = mix(c.a, 1.0, u_forceOpaque);");
            if (lut)
            {
                builder.AppendLine("    vec3 straight = c.a > 0.0 ? c.rgb / c.a : c.rgb;");
                builder.AppendLine("    float n = float(textureSize(u_lut, 0).x);");
                builder.AppendLine("    vec3 lc = clamp(straight, 0.0, 1.0) * ((n - 1.0) / n) + 0.5 / n;");
                builder.AppendLine("    c.rgb = texture(u_lut, lc).rgb * c.a;");
            }

            builder.AppendLine("    return c;");
            builder.AppendLine("}");
        }

        builder.AppendLine(source);
        builder.AppendLine("void main() { color = basin_pixel(v_uv * u_size) * u_alpha; }");
        return builder.ToString();
    }
}
