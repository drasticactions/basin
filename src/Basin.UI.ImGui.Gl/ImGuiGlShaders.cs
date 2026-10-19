namespace Basin.UI.ImGui;

public static class ImGuiGlShaders
{
    public const string SourceVersion = "Dear ImGui 1.92.2b";

    public const string SourceFile = "backends/imgui_impl_opengl3.cpp";

    public const string SourceLicense = "MIT";

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
