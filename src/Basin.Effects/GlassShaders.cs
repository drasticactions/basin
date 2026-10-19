namespace Basin.Effects;

internal static class GlassShaders
{
    internal const string VulkanHeader = """
        #version 450
        layout(push_constant) uniform Push {
            vec4 box;
            vec4 clampBox;
            vec4 hole;
            vec4 highlight;
            vec4 shape;
            vec4 light;
            vec4 tint;
            vec4 extra;
        } pc;
        layout(set = 0, binding = 0) uniform sampler2D src;
        layout(set = 1, binding = 0) uniform sampler2D plain;
        layout(location = 0) out vec4 color;
        """;

    internal const string VulkanMeshInput = """
        layout(location = 0) in vec2 v_local;
        """;

    internal const string GlMeshInput = """
        in vec2 v_local;
        """;

    internal const string GlMeshVertex = """
        #version 300 es
        layout(location = 0) in vec2 a_position;
        layout(location = 1) in vec2 a_local;
        out vec2 v_local;
        void main() {
            v_local = a_local;
            gl_Position = vec4(a_position, 0.0, 1.0);
        }
        """;

    internal const string VulkanMeshVertex = """
        #version 450
        layout(location = 0) in vec2 a_position;
        layout(location = 1) in vec2 a_local;
        layout(location = 0) out vec2 v_local;
        void main() {
            v_local = a_local;
            gl_Position = vec4(a_position, 0.0, 1.0);
        }
        """;

    internal const string GlHeader = """
        #version 300 es
        precision highp float;
        precision highp int;
        struct Push {
            vec4 box;
            vec4 clampBox;
            vec4 hole;
            vec4 highlight;
            vec4 shape;
            vec4 light;
            vec4 tint;
            vec4 extra;
        };
        uniform Push pc;
        uniform sampler2D src;
        uniform sampler2D plain;
        out vec4 color;
        """;

    internal const string Common = """
        float field(vec2 p, out vec2 n) {
            vec2 e = pc.box.zw * 0.5;
            vec2 d = p - pc.box.xy - e;
            float r = clamp(pc.shape.x, 0.0, min(e.x, e.y));
            vec2 q = abs(d) - e + vec2(r);
            vec2 s = vec2(d.x < 0.0 ? -1.0 : 1.0, d.y < 0.0 ? -1.0 : 1.0);
            if (q.x > 0.0 && q.y > 0.0) {
                n = s * normalize(q);
            } else {
                n = q.x > q.y ? vec2(s.x, 0.0) : vec2(0.0, s.y);
            }
            return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
        }
        vec2 lens(vec2 p, vec2 q, mat2 toScreen, out float d, out vec2 n, out float t) {
            d = field(q, n);
            t = 0.0;
            float bevel = pc.shape.y;
            vec2 offset = vec2(0.0);
            if (bevel > 0.0 && d <= 0.0 && d > -bevel) {
                t = 1.0 + d / bevel;
                offset = -n * (pc.shape.z * bevel * t * t);
            }
            return clamp(p + toScreen * offset, pc.clampBox.xy, pc.clampBox.zw);
        }
        float holeField(vec2 p) {
            vec2 e = pc.hole.zw * 0.5;
            vec2 d = p - pc.hole.xy - e;
            float r = clamp(pc.light.w, 0.0, min(e.x, e.y));
            vec2 q = abs(d) - e + vec2(r);
            return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
        }
        vec3 unpackRgb(float v) {
            float r = floor(v / 65536.0);
            float g = floor((v - r * 65536.0) / 256.0);
            return vec3(r, g, v - r * 65536.0 - g * 256.0) / 255.0;
        }
        uint grain(uvec2 v) {
            v = v * 1664525u + 1013904223u;
            v.x += v.y * 1664525u;
            v.y += v.x * 1664525u;
            v ^= v >> 16u;
            v.x += v.y * 1664525u;
            v.y += v.x * 1664525u;
            v ^= v >> 16u;
            return v.x;
        }
        vec4 finish(vec2 p, vec2 q, float d, vec2 n, float t, float aa, vec3 blurred) {
            float luma = dot(blurred, vec3(0.2126, 0.7152, 0.0722));
            vec3 c = mix(vec3(luma), blurred, pc.extra.x);
            c = mix(c, unpackRgb(pc.tint.x), pc.tint.z);
            vec2 toLight = pc.light.xy;
            vec2 size = vec2(textureSize(plain, 0));
            float spec = pc.shape.w;
            float face = clamp(0.5 + dot((p - 0.5 * size) / length(size), toLight), 0.0, 1.0);
            float facing = dot(n, toLight);
            c += vec3(spec * 0.12 * face * face);
            c += vec3(spec * t * t * max(facing, 0.0));
            c -= vec3(spec * 0.2 * t * t * max(-facing, 0.0));
            bool holed = pc.hole.z > 0.0 && pc.hole.w > 0.0;
            float hd = holed ? holeField(q) : 1.0e6;
            float glint = pc.highlight.z;
            if (glint > 0.0) {
                float g = length(q - pc.highlight.xy) / max(pc.highlight.w, 1.0);
                c += vec3(glint * 0.5 * exp(-4.0 * g * g) * clamp(1.0 + hd / aa, 0.0, 1.0));
            }
            float ring = pc.light.z;
            if (holed && ring > 0.0) {
                float edge = clamp(0.5 - (abs(hd - 0.5 * ring) - 0.5 * ring) / aa, 0.0, 1.0);
                c = mix(c, unpackRgb(pc.tint.y), pc.tint.w * edge);
            }
            c += vec3(float(grain(uvec2(p)) & 0xFFFFu) / 65535.0 * pc.extra.y);
            float coverage = clamp(0.5 - d / aa, 0.0, 1.0);
            vec3 under = texture(plain, p / size).rgb;
            return vec4(mix(under, clamp(c, 0.0, 1.0), coverage), 1.0);
        }
        vec3 fused(vec2 sp) {
            float f = pc.extra.z;
            float sigma = pc.extra.w;
            vec2 small = vec2(textureSize(src, 0));
            vec4 valid = vec4(floor(pc.clampBox.xy / f) + 0.5, ceil(pc.clampBox.zw / f) - 0.5);
            vec2 center = sp / f;
            float spacing = 0.5 * sigma;
            vec3 sum = vec3(0.0);
            float total = 0.0;
            for (int j = -4; j <= 4; j++) {
                for (int i = -4; i <= 4; i++) {
                    vec2 o = vec2(float(i), float(j)) * spacing;
                    float w = exp(-dot(o, o) / (2.0 * sigma * sigma));
                    vec2 at = clamp(center + o, valid.xy, max(valid.xy, valid.zw));
                    sum += texture(src, at / small).rgb * w;
                    total += w;
                }
            }
            return sum / total;
        }
        """;

    internal const string Composite = Common + """

        void main() {
            vec2 p = gl_FragCoord.xy;
            float d;
            vec2 n;
            float t;
            vec2 sp = lens(p, p, mat2(1.0), d, n, t);
            color = finish(p, p, d, n, t, 1.0, texture(src, sp / vec2(textureSize(src, 0))).rgb);
        }
        """;

    internal const string Fused = Common + """

        void main() {
            vec2 p = gl_FragCoord.xy;
            float d;
            vec2 n;
            float t;
            vec2 sp = lens(p, p, mat2(1.0), d, n, t);
            color = finish(p, p, d, n, t, 1.0, fused(sp));
        }
        """;

    internal const string MeshMain = """

        mat2 toScreen(vec2 q) {
            mat2 toLocal = mat2(dFdx(q), dFdy(q));
            return abs(determinant(toLocal)) > 1.0e-8 ? inverse(toLocal) : mat2(1.0);
        }
        """;

    internal const string MeshComposite = Common + MeshMain + """
        void main() {
            vec2 p = gl_FragCoord.xy;
            vec2 q = v_local;
            mat2 back = toScreen(q);
            float d;
            vec2 n;
            float t;
            vec2 sp = lens(p, q, back, d, n, t);
            color = finish(p, q, d, n, t, max(fwidth(d), 1.0e-4), texture(src, sp / vec2(textureSize(src, 0))).rgb);
        }
        """;

    internal const string MeshFused = Common + MeshMain + """
        void main() {
            vec2 p = gl_FragCoord.xy;
            vec2 q = v_local;
            mat2 back = toScreen(q);
            float d;
            vec2 n;
            float t;
            vec2 sp = lens(p, q, back, d, n, t);
            color = finish(p, q, d, n, t, max(fwidth(d), 1.0e-4), fused(sp));
        }
        """;

    internal const string Copy = """
        void main() {
            color = texture(plain, gl_FragCoord.xy / vec2(textureSize(plain, 0)));
        }
        """;

    internal const string FusedDown = """
        void main() {
            float f = pc.extra.z;
            vec2 origin = floor(gl_FragCoord.xy) * f;
            int count = f <= 2.0 ? 1 : int(min(f * 0.5, 8.0));
            float step = f / float(count);
            vec4 sum = vec4(0.0);
            for (int j = 0; j < 8; j++) {
                if (j >= count) {
                    break;
                }
                for (int i = 0; i < 8; i++) {
                    if (i >= count) {
                        break;
                    }
                    vec2 at = clamp(origin + (vec2(float(i), float(j)) + 0.5) * step, pc.clampBox.xy, pc.clampBox.zw);
                    sum += texture(src, at / vec2(textureSize(src, 0)));
                }
            }
            color = sum / float(count * count);
        }
        """;
}
