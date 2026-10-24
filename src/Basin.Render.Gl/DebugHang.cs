using Silk.NET.OpenGLES;

namespace Basin.Render.Gl;

internal static class DebugHang
{
    private const string Vertex = """
        #version 300 es
        void main() {
            vec2 corner = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
            gl_Position = vec4(corner * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    private const string Fragment = """
        #version 300 es
        precision highp float;
        uniform float u_stop;
        out vec4 color;
        void main() {
            float x = 0.0;
            while (x < u_stop) {
                x = x * 1.0000001;
            }
            color = vec4(x);
        }
        """;

#if DEBUG
    private static int _remaining =
        int.TryParse(Environment.GetEnvironmentVariable("BASIN_DEBUG_GPU_HANG"), out var passes) && passes > 0 ? passes : 0;
#endif

    public static void Submit(GL gl)
    {
#if DEBUG
        if (_remaining <= 0 || --_remaining > 0)
        {
            return;
        }

        var program = new ShaderProgram(gl, Vertex, Fragment);
        var texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, texture);
        gl.TexStorage2D(TextureTarget.Texture2D, 1, SizedInternalFormat.Rgba8, 64, 64);
        var framebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
        gl.Viewport(0, 0, 64, 64);
        gl.UseProgram(program.Program);
        gl.Uniform1(gl.GetUniformLocation(program.Program, "u_stop"), 1.0f);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        gl.Flush();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
#else
        _ = gl;
#endif
    }
}
