using Silk.NET.OpenGLES;

namespace Basin.Render.Gl;

internal sealed class GlNvProgram
{
    private readonly ShaderProgram _program;
    private readonly int _coeffs;

    public GlNvProgram(GL gl, uint vao)
    {
        _program = new ShaderProgram(gl, GlShaders.Vertex, GlShaders.NvToRgbFragment);
        _coeffs = gl.GetUniformLocation(_program.Program, "u_coeffs");
        var chroma = gl.GetUniformLocation(_program.Program, "u_chroma");
        gl.UseProgram(_program.Program);
        gl.Uniform1(chroma, 1);
        Vao = vao;
    }

    public uint Vao { get; }

    public unsafe void Draw(GL gl, int width, int height, YuvCoefficients coefficients, YuvRange range)
    {
        var (kr, kb) = YuvConversion.Weights(coefficients);
        gl.UseProgram(_program.Program);
        gl.Uniform4(_program.Dst, 0f, 0f, (float)width, height);
        gl.Uniform2(_program.Target, (float)width, height);
        var identity = stackalloc float[9] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        gl.UniformMatrix3(_program.Transform, 1, false, identity);
        gl.Uniform4(_coeffs, kr, kb, range == YuvRange.Limited ? 1f : 0f, 0f);
        gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
    }

    public void Dispose(GL gl) => _program.Dispose(gl);
}
