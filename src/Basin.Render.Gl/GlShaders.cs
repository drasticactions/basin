using Pixman;
using Silk.NET.OpenGLES;

namespace Basin.Render.Gl;

internal static class GlShaders
{
    public const string Vertex = """
        #version 300 es
        uniform vec4 u_dst;     // x, y, w, h in target pixels, y-down
        uniform vec2 u_target;  // target size in pixels
        uniform mat3 u_transform;
        out vec2 v_uv;
        void main() {
            vec2 corner = vec2(float(gl_VertexID & 1), float((gl_VertexID >> 1) & 1));
            v_uv = corner;
            vec2 px = u_dst.xy + corner * u_dst.zw;
            vec3 tp = u_transform * vec3(px, 1.0);
            gl_Position = vec4(tp.xy / u_target * 2.0 - tp.z, 0.0, tp.z);
        }
        """;

    public const string TextureFragment = """
        #version 300 es
        precision highp float;
        uniform sampler2D u_texture;
        uniform vec4 u_src;         // normalized source box
        uniform float u_alpha;
        uniform float u_forceOpaque;
        in vec2 v_uv;
        out vec4 color;
        void main() {
            vec4 c = texture(u_texture, u_src.xy + v_uv * u_src.zw);
            c.a = mix(c.a, 1.0, u_forceOpaque);
            color = c * u_alpha;    // premultiplied
        }
        """;

    public const string TextureLutFragment = """
        #version 300 es
        precision highp float;
        precision highp sampler3D;
        uniform sampler2D u_texture;
        uniform sampler3D u_lut;
        uniform vec4 u_src;
        uniform float u_alpha;
        uniform float u_forceOpaque;
        in vec2 v_uv;
        out vec4 color;
        void main() {
            vec4 c = texture(u_texture, u_src.xy + v_uv * u_src.zw);
            c.a = mix(c.a, 1.0, u_forceOpaque);
            vec3 straight = c.a > 0.0 ? c.rgb / c.a : c.rgb;
            float n = float(textureSize(u_lut, 0).x);
            vec3 coord = clamp(straight, 0.0, 1.0) * ((n - 1.0) / n) + 0.5 / n;
            c.rgb = texture(u_lut, coord).rgb * c.a;
            color = c * u_alpha;    // premultiplied
        }
        """;

    public const string SolidFragment = """
        #version 300 es
        precision highp float;
        uniform vec4 u_color;       // premultiplied
        out vec4 color;
        void main() { color = u_color; }
        """;

    public const string MeshVertex = """
        #version 300 es
        uniform vec2 u_target;
        layout(location = 0) in vec2 a_pos;
        layout(location = 1) in vec2 a_uv;
        layout(location = 2) in vec4 a_color;
        out vec2 v_uv;
        out vec4 v_color;
        void main() {
            v_uv = a_uv;
            v_color = a_color;
            gl_Position = vec4(a_pos / u_target * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    public const string MeshFragment = """
        #version 300 es
        precision highp float;
        uniform sampler2D u_texture;
        uniform vec4 u_src;         // xy unused, zw = texture size in pixels
        uniform float u_hasTexture;
        uniform float u_forceOpaque;
        in vec2 v_uv;
        in vec4 v_color;
        out vec4 color;
        void main() {
            vec4 c = vec4(1.0);
            if (u_hasTexture > 0.5) {
                c = texture(u_texture, v_uv / u_src.zw);
                c.a = mix(c.a, 1.0, u_forceOpaque);
            }
            color = c * v_color;
        }
        """;
}
