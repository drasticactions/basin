namespace Basin.UI.ImGui;

/// <summary>
/// The shaders of the Dear ImGui OpenGL backend, which the OpenGL renderer of the ImGui host compiles. The constants name the source of the shaders.
/// </summary>
public static class ImGuiGlShaders
{
    /// <summary>
    /// The Dear ImGui release that the shaders come from.
    /// </summary>
    public const string SourceVersion = "Dear ImGui 1.92.2b";

    /// <summary>
    /// The file in the Dear ImGui tree that holds the original shaders.
    /// </summary>
    public const string SourceFile = "backends/imgui_impl_opengl3.cpp";

    /// <summary>
    /// The license of the original shaders.
    /// </summary>
    public const string SourceLicense = "MIT";

    /// <summary>
    /// The GLSL ES 3.00 vertex shader. It maps the vertex position through the ProjMtx uniform and passes the UV and the color on.
    /// </summary>
    public const string Vertex = """
        #version 300 es
        precision highp float;
        layout (location = 0) in vec2 Position;
        layout (location = 1) in vec2 UV;
        layout (location = 2) in vec4 Color;
        uniform mat4 ProjMtx;
        out vec2 Frag_UV;
        out vec4 Frag_Color;
        void main()
        {
            Frag_UV = UV;
            Frag_Color = Color;
            gl_Position = ProjMtx * vec4(Position.xy,0,1);
        }
        """;

    /// <summary>
    /// The GLSL ES 3.00 fragment shader. It multiplies the vertex color by the texture sample.
    /// </summary>
    public const string Fragment = """
        #version 300 es
        precision mediump float;
        uniform sampler2D Texture;
        in vec2 Frag_UV;
        in vec4 Frag_Color;
        layout (location = 0) out vec4 Out_Color;
        void main()
        {
            Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
        }
        """;
}
