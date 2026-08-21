using Pixman;
using Silk.NET.OpenGLES;

namespace Basin.Render.Gl;

internal sealed class ShaderProgram
{
    public readonly uint Program;
    public readonly int Dst;
    public readonly int Target;
    public readonly int Src;
    public readonly int Alpha;
    public readonly int ForceOpaque;
    public readonly int Color;
    public readonly int Lut;
    public readonly int Transform;
    public readonly int HasTexture;
    public readonly int Size;

    public ShaderProgram(GL gl, string vertexSource, string fragmentSource)
    {
        Program = gl.CreateProgram();
        foreach (var (type, source) in new[] { (ShaderType.VertexShader, vertexSource), (ShaderType.FragmentShader, fragmentSource) })
        {
            var shader = gl.CreateShader(type);
            gl.ShaderSource(shader, source);
            gl.CompileShader(shader);
            gl.GetShader(shader, ShaderParameterName.CompileStatus, out var compiled);
            if (compiled == 0)
            {
                throw new InvalidOperationException($"{type}: {gl.GetShaderInfoLog(shader)}");
            }

            gl.AttachShader(Program, shader);
            gl.DeleteShader(shader);
        }

        gl.LinkProgram(Program);
        gl.GetProgram(Program, ProgramPropertyARB.LinkStatus, out var linked);
        if (linked == 0)
        {
            throw new InvalidOperationException($"link: {gl.GetProgramInfoLog(Program)}");
        }

        Dst = gl.GetUniformLocation(Program, "u_dst");
        Target = gl.GetUniformLocation(Program, "u_target");
        Src = gl.GetUniformLocation(Program, "u_src");
        Alpha = gl.GetUniformLocation(Program, "u_alpha");
        ForceOpaque = gl.GetUniformLocation(Program, "u_forceOpaque");
        Color = gl.GetUniformLocation(Program, "u_color");
        Lut = gl.GetUniformLocation(Program, "u_lut");
        Transform = gl.GetUniformLocation(Program, "u_transform");
        HasTexture = gl.GetUniformLocation(Program, "u_hasTexture");
        Size = gl.GetUniformLocation(Program, "u_size");
    }

    public void Dispose(GL gl) => gl.DeleteProgram(Program);
}
