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

    public const string Finish = """
        uniform vec4 u_corner;
        uniform vec2 u_cornerOuter;
        uniform vec2 u_dimming;
        uniform vec2 u_finishSize;
        vec4 basin_finish(vec4 c) {
            if (u_dimming.x != 0.0 || u_dimming.y != 0.0) {
                vec3 straight = c.a > 0.0 ? c.rgb / c.a : c.rgb;
                float luma = dot(straight, vec3(0.2126, 0.7152, 0.0722));
                c.rgb = mix(vec3(luma), straight, 1.0 - u_dimming.y) * (1.0 - u_dimming.x) * c.a;
            }
            if (u_corner.x > 0.0) {
                vec2 coord = v_uv * u_finishSize;
                vec2 halfSize = (u_cornerOuter.x > 0.0 ? u_cornerOuter : u_finishSize) * 0.5;
                vec2 p = abs(coord + u_corner.yz - halfSize) - (halfSize - vec2(u_corner.x));
                float d = length(max(p, vec2(0.0))) - u_corner.x;
                c *= clamp(0.5 - d, 0.0, 1.0);
            }
            return c;
        }
        """;

    public const string TextureFragment = $$"""
        #version 300 es
        precision highp float;
        uniform sampler2D u_texture;
        uniform vec4 u_src;         // normalized source box
        uniform float u_alpha;
        uniform float u_forceOpaque;
        uniform float u_pixel;
        vec2 pixel_uv(vec2 uv) {
            if (u_pixel < 0.5) {
                return uv;
            }

            vec2 size = vec2(textureSize(u_texture, 0));
            vec2 extent = max(fwidth(uv * size), vec2(1.0 / 256.0));
            vec2 pixel = uv * size - 0.5;
            vec2 base = floor(pixel);
            vec2 phase = pixel - base;
            vec2 shift = 0.5 + 0.5 * sin(1.5707963 * clamp((phase - 0.5) / min(extent, vec2(0.25)), -1.0, 1.0));
            return (base + 0.5 + shift) / size;
        }
        in vec2 v_uv;
        out vec4 color;
        {{Finish}}
        void main() {
            vec4 c = texture(u_texture, pixel_uv(u_src.xy + v_uv * u_src.zw));
            c.a = mix(c.a, 1.0, u_forceOpaque);
            color = basin_finish(c) * u_alpha;    // premultiplied
        }
        """;

    public const string TextureLutFragment = $$"""
        #version 300 es
        precision highp float;
        precision highp sampler3D;
        uniform sampler2D u_texture;
        uniform sampler3D u_lut;
        uniform vec4 u_src;
        uniform float u_alpha;
        uniform float u_forceOpaque;
        uniform float u_pixel;
        vec2 pixel_uv(vec2 uv) {
            if (u_pixel < 0.5) {
                return uv;
            }

            vec2 size = vec2(textureSize(u_texture, 0));
            vec2 extent = max(fwidth(uv * size), vec2(1.0 / 256.0));
            vec2 pixel = uv * size - 0.5;
            vec2 base = floor(pixel);
            vec2 phase = pixel - base;
            vec2 shift = 0.5 + 0.5 * sin(1.5707963 * clamp((phase - 0.5) / min(extent, vec2(0.25)), -1.0, 1.0));
            return (base + 0.5 + shift) / size;
        }
        uniform float u_shaper;
        vec3 lut_fetch(ivec3 p) {
            return texelFetch(u_lut, p, 0).rgb;
        }

        vec3 lut_tetrahedral(vec3 coord) {
            int n = textureSize(u_lut, 0).x;
            vec3 f = clamp(coord, 0.0, 1.0) * float(n - 1);
            ivec3 base = min(ivec3(f), ivec3(n - 2));
            vec3 t = f - vec3(base);
            vec3 c000 = lut_fetch(base);
            vec3 c111 = lut_fetch(base + ivec3(1));
            if (t.x >= t.y) {
                if (t.y >= t.z) {
                    vec3 c100 = lut_fetch(base + ivec3(1, 0, 0));
                    vec3 c110 = lut_fetch(base + ivec3(1, 1, 0));
                    return c000 + t.x * (c100 - c000) + t.y * (c110 - c100) + t.z * (c111 - c110);
                }
                if (t.x >= t.z) {
                    vec3 c100 = lut_fetch(base + ivec3(1, 0, 0));
                    vec3 c101 = lut_fetch(base + ivec3(1, 0, 1));
                    return c000 + t.x * (c100 - c000) + t.z * (c101 - c100) + t.y * (c111 - c101);
                }
                vec3 c001 = lut_fetch(base + ivec3(0, 0, 1));
                vec3 c101 = lut_fetch(base + ivec3(1, 0, 1));
                return c000 + t.z * (c001 - c000) + t.x * (c101 - c001) + t.y * (c111 - c101);
            }
            if (t.x >= t.z) {
                vec3 c010 = lut_fetch(base + ivec3(0, 1, 0));
                vec3 c110 = lut_fetch(base + ivec3(1, 1, 0));
                return c000 + t.y * (c010 - c000) + t.x * (c110 - c010) + t.z * (c111 - c110);
            }
            if (t.y >= t.z) {
                vec3 c010 = lut_fetch(base + ivec3(0, 1, 0));
                vec3 c011 = lut_fetch(base + ivec3(0, 1, 1));
                return c000 + t.y * (c010 - c000) + t.z * (c011 - c010) + t.x * (c111 - c011);
            }
            vec3 c001 = lut_fetch(base + ivec3(0, 0, 1));
            vec3 c011 = lut_fetch(base + ivec3(0, 1, 1));
            return c000 + t.z * (c001 - c000) + t.y * (c011 - c001) + t.x * (c111 - c011);
        }

        vec3 pq_inverse(vec3 nits) {
            vec3 y = pow(clamp(nits / 10000.0, 0.0, 1.0), vec3(2610.0 / 16384.0));
            return pow((3424.0 / 4096.0 + 2413.0 / 4096.0 * 32.0 * y) / (1.0 + 2392.0 / 4096.0 * 32.0 * y), vec3(2523.0 / 4096.0 * 128.0));
        }

        vec3 shape(vec3 straight, float shaperNits) {
            return shaperNits > 0.0 ? pq_inverse(max(straight, vec3(0.0)) * shaperNits) : clamp(straight, 0.0, 1.0);
        }

        in vec2 v_uv;
        out vec4 color;
        {{Finish}}
        void main() {
            vec4 c = texture(u_texture, pixel_uv(u_src.xy + v_uv * u_src.zw));
            c.a = mix(c.a, 1.0, u_forceOpaque);
            vec3 straight = c.a > 0.0 ? c.rgb / c.a : c.rgb;
            c.rgb = lut_tetrahedral(shape(straight, u_shaper)) * c.a;
            color = basin_finish(c) * u_alpha;    // premultiplied
        }
        """;

    public const string TextureColorFragment = $$"""
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
        uniform float u_pixel;
        vec2 pixel_uv(vec2 uv) {
            if (u_pixel < 0.5) {
                return uv;
            }

            vec2 size = vec2(textureSize(u_texture, 0));
            vec2 extent = max(fwidth(uv * size), vec2(1.0 / 256.0));
            vec2 pixel = uv * size - 0.5;
            vec2 base = floor(pixel);
            vec2 phase = pixel - base;
            vec2 shift = 0.5 + 0.5 * sin(1.5707963 * clamp((phase - 0.5) / min(extent, vec2(0.25)), -1.0, 1.0));
            return (base + 0.5 + shift) / size;
        }
        in vec2 v_uv;
        out vec4 color;
        {{Finish}}
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
            vec4 c = texture(u_texture, pixel_uv(u_src.xy + v_uv * u_src.zw));
            c.a = mix(c.a, 1.0, u_forceOpaque);
            vec3 straight = clamp(c.a > 0.0 ? c.rgb / c.a : c.rgb, 0.0, int(u_source.x) == 2 ? u_source.y : 1.0);
            vec3 nits = decode(u_source, straight) * u_source.w;
            nits = max(vec3(dot(u_m0, nits), dot(u_m1, nits), dot(u_m2, nits)), vec3(0.0));
            if (u_tone.x > 0.5) {
                float peak = max(nits.r, max(nits.g, nits.b));
                if (peak > 0.0) {
                    nits *= bt2390(peak) / peak;
                }
            }
            c.rgb = clamp(encode(u_output, nits), 0.0, 1.0) * c.a;
            color = basin_finish(c) * u_alpha;    // premultiplied
        }
        """;

    public const string YuvFragment = """
        #version 300 es
        precision highp float;
        uniform sampler2D u_texture;
        uniform vec4 u_src;
        uniform vec4 u_rect;
        uniform vec4 u_coeffs;
        out vec4 color;
        vec3 fetch(vec2 luma) {
            vec2 rel = (luma - u_rect.xy) / u_rect.zw;
            if (any(lessThan(rel, vec2(0.0))) || any(greaterThanEqual(rel, vec2(1.0)))) {
                return vec3(0.0);
            }
            return texture(u_texture, u_src.xy + rel * u_src.zw).rgb;
        }
        void main() {
            float kr = u_coeffs.x;
            float kb = u_coeffs.y;
            vec3 weights = vec3(kr, 1.0 - kr - kb, kb);
            vec2 p = floor(gl_FragCoord.xy);
            if (u_coeffs.w < 0.5) {
                float y = dot(fetch(p + 0.5), weights);
                y = u_coeffs.z > 0.5 ? (16.0 + 219.0 * y) / 255.0 : y;
                color = vec4(y, 0.0, 0.0, 1.0);
                return;
            }
            vec2 base = p * 2.0;
            vec3 rgb = (fetch(base + vec2(0.5, 0.5)) + fetch(base + vec2(1.5, 0.5))
                + fetch(base + vec2(0.5, 1.5)) + fetch(base + vec2(1.5, 1.5))) * 0.25;
            float y = dot(rgb, weights);
            float cb = (rgb.b - y) / (2.0 * (1.0 - kb));
            float cr = (rgb.r - y) / (2.0 * (1.0 - kr));
            vec2 c = u_coeffs.z > 0.5 ? (128.0 + 224.0 * vec2(cb, cr)) / 255.0 : vec2(cb, cr) + 0.5;
            color = vec4(c, 0.0, 1.0);
        }
        """;

    public const string NvToRgbFragment = """
        #version 300 es
        precision highp float;
        uniform sampler2D u_texture;
        uniform sampler2D u_chroma;
        uniform vec4 u_coeffs;
        in vec2 v_uv;
        out vec4 color;
        void main() {
            float kr = u_coeffs.x;
            float kb = u_coeffs.y;
            float kg = 1.0 - kr - kb;
            float y = texture(u_texture, v_uv).r;
            vec2 c = texture(u_chroma, v_uv).rg;
            if (u_coeffs.z > 0.5) {
                y = (y * 255.0 - 16.0) / 219.0;
                c = (c * 255.0 - 128.0) / 224.0;
            } else {
                c = c - 128.0 / 255.0;
            }
            float r = y + 2.0 * (1.0 - kr) * c.y;
            float b = y + 2.0 * (1.0 - kb) * c.x;
            float g = (y - kr * r - kb * b) / kg;
            color = vec4(clamp(vec3(r, g, b), 0.0, 1.0), 1.0);
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
