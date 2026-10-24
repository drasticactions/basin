using Silk.NET.OpenGLES;

namespace Basin.Render3D.Gl;

internal sealed class GlWorldProgram
{
    public GlWorldProgram(GL gl, string vertexSource, string fragmentSource)
    {
        Program = gl.CreateProgram();
        Attach(gl, ShaderType.VertexShader, vertexSource);
        Attach(gl, ShaderType.FragmentShader, fragmentSource);
        gl.LinkProgram(Program);
        gl.GetProgram(Program, ProgramPropertyARB.LinkStatus, out var linked);
        if (linked == 0)
        {
            var log = gl.GetProgramInfoLog(Program);
            gl.DeleteProgram(Program);
            throw new InvalidOperationException($"The world shader program did not link: {log}");
        }

        Mvp = gl.GetUniformLocation(Program, "u_mvp");
        Model = gl.GetUniformLocation(Program, "u_model");
        UvRect = gl.GetUniformLocation(Program, "u_uvRect");
        Texture = gl.GetUniformLocation(Program, "u_texture");
        Tint = gl.GetUniformLocation(Program, "u_tint");
        Textured = gl.GetUniformLocation(Program, "u_textured");
        Lit = gl.GetUniformLocation(Program, "u_lit");
        ForceOpaque = gl.GetUniformLocation(Program, "u_forceOpaque");
        Ambient = gl.GetUniformLocation(Program, "u_ambient");
        Eye = gl.GetUniformLocation(Program, "u_eye");
        Emissive = gl.GetUniformLocation(Program, "u_emissive");
        Specular = gl.GetUniformLocation(Program, "u_specular");
        LightCount = gl.GetUniformLocation(Program, "u_lightCount");
        LightPosition = gl.GetUniformLocation(Program, "u_lightPosition");
        LightColor = gl.GetUniformLocation(Program, "u_lightColor");
        Joints = gl.GetUniformLocation(Program, "u_joints");
        Far = gl.GetUniformLocation(Program, "u_far");
        Color = gl.GetUniformLocation(Program, "u_color");
    }

    public uint Program { get; }

    public int Mvp { get; }

    public int Model { get; }

    public int UvRect { get; }

    public int Texture { get; }

    public int Tint { get; }

    public int Textured { get; }

    public int Lit { get; }

    public int ForceOpaque { get; }

    public int Ambient { get; }

    public int Eye { get; }

    public int Emissive { get; }

    public int Specular { get; }

    public int LightCount { get; }

    public int LightPosition { get; }

    public int LightColor { get; }

    public int Joints { get; }

    public int Far { get; }

    public int Color { get; }

    public void Dispose(GL gl) => gl.DeleteProgram(Program);

    private void Attach(GL gl, ShaderType type, string source)
    {
        var shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);
        gl.GetShader(shader, ShaderParameterName.CompileStatus, out var compiled);
        if (compiled == 0)
        {
            var log = gl.GetShaderInfoLog(shader);
            gl.DeleteShader(shader);
            throw new InvalidOperationException($"The world {type} did not compile: {log}");
        }

        gl.AttachShader(Program, shader);
        gl.DeleteShader(shader);
    }
}
