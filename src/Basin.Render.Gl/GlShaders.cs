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

    public const string TextureColorFragment = """
        #version 300 es
        precision highp float;
        uniform sampler2D u_texture;
        uniform vec4 u_src;
        uniform float u_alpha;
        uniform float u_forceOpaque;
        uniform vec3 u_m0;
        uniform vec3 u_m1;
        uniform vec3 u_m2;
        uniform vec4 u_source;
        uniform vec4 u_output;
        uniform vec4 u_tone;
        in vec2 v_uv;
        out vec4 color;
        const float PQ_M1 = 2610.0 / 16384.0;
        const float PQ_M2 = 2523.0 / 4096.0 * 128.0;
        const float PQ_C1 = 3424.0 / 4096.0;
        const float PQ_C2 = 2413.0 / 4096.0 * 32.0;
        const float PQ_C3 = 2392.0 / 4096.0 * 32.0;
        const float HLG_A = 0.17883277;
        const float HLG_B = 0.28466892;
        const float HLG_C = 0.55991073;
        vec3 srgb_to_linear(vec3 c) {
            return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
        }
        vec3 compound_inverse(vec3 l) {
            return mix(l * 12.92, 1.055 * pow(l, vec3(1.0 / 2.4)) - 0.055, step(0.0031308, l));
        }
        vec3 pq_eotf(vec3 s) {
            vec3 p = pow(s, vec3(1.0 / PQ_M2));
            vec3 num = max(p - PQ_C1, vec3(0.0));
            return 10000.0 * pow(num / (PQ_C2 - PQ_C3 * p), vec3(1.0 / PQ_M1));
        }
        float pq_eotf1(float s) {
            float p = pow(s, 1.0 / PQ_M2);
            float num = max(p - PQ_C1, 0.0);
            return 10000.0 * pow(num / (PQ_C2 - PQ_C3 * p), 1.0 / PQ_M1);
        }
        vec3 pq_inverse(vec3 nits) {
            vec3 y = pow(clamp(nits / 10000.0, 0.0, 1.0), vec3(PQ_M1));
            return pow((PQ_C1 + PQ_C2 * y) / (1.0 + PQ_C3 * y), vec3(PQ_M2));
        }
        float pq_inverse1(float nits) {
            float y = pow(clamp(nits / 10000.0, 0.0, 1.0), PQ_M1);
            return pow((PQ_C1 + PQ_C2 * y) / (1.0 + PQ_C3 * y), PQ_M2);
        }
        vec3 hlg_inverse_oetf(vec3 s) {
            return mix(s * s / 3.0, (exp((s - HLG_C) / HLG_A) + HLG_B) / 12.0, step(0.5, s));
        }
        vec3 hlg_oetf(vec3 e) {
            return mix(sqrt(3.0 * e), HLG_A * log(max(12.0 * e - HLG_B, 1e-6)) + HLG_C, step(1.0 / 12.0, e));
        }
        vec3 decode(vec4 tf, vec3 s) {
            int kind = int(tf.x);
            if (kind == 0) return srgb_to_linear(s) * tf.z;
            if (kind == 1) return pow(s, vec3(tf.y)) * tf.z;
            if (kind == 2) return s * tf.z;
            if (kind == 3) return pq_eotf(s);
            return pow(hlg_inverse_oetf(s), vec3(1.2)) * tf.z;
        }
        vec3 encode(vec4 tf, vec3 nits) {
            int kind = int(tf.x);
            nits = max(nits, vec3(0.0));
            vec3 relative = min(nits / tf.z, vec3(1.0));
            if (kind == 0) return compound_inverse(relative);
            if (kind == 1) return pow(relative, vec3(1.0 / tf.y));
            if (kind == 2) return relative;
            if (kind == 3) return pq_inverse(nits);
            return hlg_oetf(pow(relative, vec3(1.0 / 1.2)));
        }
        float bt2390(float nits) {
            float e1 = min(1.0, pq_inverse1(nits) / u_tone.y);
            if (e1 <= u_tone.w) return nits;
            float t = (e1 - u_tone.w) / (1.0 - u_tone.w);
            float t2 = t * t;
            float t3 = t2 * t;
            float e2 = (2.0 * t3 - 3.0 * t2 + 1.0) * u_tone.w
                + (t3 - 2.0 * t2 + t) * (1.0 - u_tone.w)
                + (-2.0 * t3 + 3.0 * t2) * u_tone.z;
            return pq_eotf1(e2 * u_tone.y);
        }
        void main() {
            vec4 c = texture(u_texture, u_src.xy + v_uv * u_src.zw);
            c.a = mix(c.a, 1.0, u_forceOpaque);
            vec3 straight = clamp(c.a > 0.0 ? c.rgb / c.a : c.rgb, 0.0, 1.0);
            vec3 nits = decode(u_source, straight) * u_source.w;
            nits = max(vec3(dot(u_m0, nits), dot(u_m1, nits), dot(u_m2, nits)), vec3(0.0));
            if (u_tone.x > 0.5) {
                float peak = max(nits.r, max(nits.g, nits.b));
                if (peak > 0.0) {
                    nits *= bt2390(peak) / peak;
                }
            }
            c.rgb = clamp(encode(u_output, nits), 0.0, 1.0) * c.a;
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
