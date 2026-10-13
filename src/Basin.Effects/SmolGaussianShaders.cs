namespace Basin.Effects;

internal static class SmolGaussianShaders
{
    internal const string VulkanHeader = """
        #version 450
        layout(push_constant) uniform Push {
            vec4 source;
            vec4 sourcePad;
            vec4 destination;
            vec4 axis;
            vec4 clampBox;
        } pc;
        layout(set = 0, binding = 0) uniform sampler2D src;
        layout(location = 0) out vec4 color;
        """;

    internal const string GlHeader = """
        #version 300 es
        precision highp float;
        struct Push {
            vec4 source;
            vec4 sourcePad;
            vec4 destination;
            vec4 axis;
            vec4 clampBox;
        };
        uniform Push pc;
        uniform sampler2D src;
        out vec4 color;
        """;

    internal const string Down = """
        vec2 span(float pixel, float scale, float origin, float size, float pad, float outSize) {
            if (pixel < 0.0) {
                return vec2(origin - pad, origin - pad + 1.0);
            }
            if (pixel >= outSize) {
                return vec2(origin + size + pad - 1.0, origin + size + pad);
            }
            return vec2(pixel * scale + origin, min((pixel + 1.0) * scale, size) + origin);
        }
        vec3 areaAxis(float lo, float hi, float physical) {
            float first = floor(lo);
            float w0 = min(hi, first + 1.0) - lo;
            float w1 = max(0.0, min(hi, first + 2.0) - (first + 1.0));
            float w2 = max(0.0, hi - (first + 2.0));
            return vec3((first + 0.5 + w1 / (w0 + w1)) / physical, (first + 2.5) / physical, w2 / (hi - lo));
        }
        void main() {
            vec2 physical = pc.sourcePad.zw;
            vec2 size = pc.source.zw;
            vec2 outSize = pc.destination.xy;
            vec2 scale = size / outSize;
            vec2 pixel = floor(gl_FragCoord.xy) - pc.destination.zw;
            vec2 sx = span(pixel.x, scale.x, pc.source.x, size.x, pc.sourcePad.x, outSize.x);
            vec2 sy = span(pixel.y, scale.y, pc.source.y, size.y, pc.sourcePad.y, outSize.y);
            bool secondX = size.x != outSize.x && size.x != 2.0 * outSize.x;
            bool secondY = size.y != outSize.y && size.y != 2.0 * outSize.y;
            if (!secondX && !secondY) {
                color = texture(src, vec2(sx.x + sx.y, sy.x + sy.y) * 0.5 / physical);
                return;
            }
            vec3 x = areaAxis(sx.x, sx.y, physical.x);
            vec3 y = areaAxis(sy.x, sy.y, physical.y);
            vec4 a = texture(src, vec2(x.x, y.x));
            if (secondX) {
                a = mix(a, texture(src, vec2(x.y, y.x)), x.z);
            }
            if (secondY) {
                vec4 b = texture(src, vec2(x.x, y.y));
                if (secondX) {
                    b = mix(b, texture(src, vec2(x.y, y.y)), x.z);
                }
                a = mix(a, b, y.z);
            }
            color = a;
        }
        """;

    internal const string Down4 = """
        vec2 place(float pixel, float scale, float origin, float size, float pad, float outSize) {
            if (pixel < 0.0) {
                return vec2(origin - pad + 0.5, 0.0);
            }
            if (pixel >= outSize) {
                return vec2(origin + size + pad - 0.5, 0.0);
            }
            return vec2((pixel + 0.5) * scale + origin, scale == 4.0 ? 1.0 : 0.0);
        }
        void main() {
            vec2 physical = pc.sourcePad.zw;
            vec2 size = pc.source.zw;
            vec2 outSize = pc.destination.xy;
            vec2 scale = size / outSize;
            vec2 pixel = floor(gl_FragCoord.xy) - pc.destination.zw;
            vec2 px = place(pixel.x, scale.x, pc.source.x, size.x, pc.sourcePad.x, outSize.x);
            vec2 py = place(pixel.y, scale.y, pc.source.y, size.y, pc.sourcePad.y, outSize.y);
            vec2 lo = vec2(px.x - px.y, py.x - py.y) / physical;
            vec2 hi = vec2(px.x + px.y, py.x + py.y) / physical;
            vec4 a = texture(src, lo);
            if (scale.x == 4.0) {
                a = 0.5 * (a + texture(src, vec2(hi.x, lo.y)));
            }
            if (scale.y == 4.0) {
                vec4 b = texture(src, vec2(lo.x, hi.y));
                if (scale.x == 4.0) {
                    b = 0.5 * (b + texture(src, hi));
                }
                a = 0.5 * (a + b);
            }
            color = a;
        }
        """;

    internal const string Gauss = """
        float weight(float i, float sigma) {
            float x = i / sigma;
            float t = clamp(4.0 - x, 0.0, 1.0);
            return exp(-0.5 * x * x) * t * t * (3.0 - 2.0 * t);
        }
        vec4 tap(vec2 position) {
            return texture(src, clamp(position, pc.clampBox.xy, pc.clampBox.zw) / pc.sourcePad.zw);
        }
        void main() {
            vec2 direction = pc.axis.xy;
            float sigma = pc.axis.z;
            int count = int(pc.axis.w);
            vec2 position = gl_FragCoord.xy;
            vec4 sum = tap(position);
            float total = 1.0;
            for (int j = 0; j < 12; j++) {
                if (j >= count) {
                    break;
                }
                float i = float(2 * j + 1);
                float a = weight(i, sigma);
                float b = weight(i + 1.0, sigma);
                float w = a + b;
                float offset = w > 0.0 ? i + b / w : i;
                sum += (tap(position - direction * offset) + tap(position + direction * offset)) * w;
                total += 2.0 * w;
            }
            color = sum / total;
        }
        """;

    internal const string Reconstruct = """
        vec3 cubicAxis(float uv, float size, float outSize, float pad) {
            if (2.0 * size >= outSize) {
                float p = uv * size + pad;
                return vec3(p, p, 0.0);
            }
            float p = uv * size - 0.5;
            float base = floor(p);
            float f = p - base;
            float w0 = (1.0 - f) * (1.0 - f) * (1.0 - f) / 6.0;
            float w1 = (3.0 * f * f * f - 6.0 * f * f + 4.0) / 6.0;
            float w2 = (-3.0 * f * f * f + 3.0 * f * f + 3.0 * f + 1.0) / 6.0;
            float w3 = f * f * f / 6.0;
            float g0 = w0 + w1;
            float g1 = w2 + w3;
            return vec3(base - 0.5 + w1 / g0 + pad, base + 1.5 + w3 / g1 + pad, g1);
        }
        vec4 tap(vec2 position) {
            return texture(src, clamp(position, pc.clampBox.xy, pc.clampBox.zw) / pc.sourcePad.zw);
        }
        void main() {
            vec2 size = pc.source.zw;
            vec2 outSize = pc.destination.zw;
            vec2 uv = (gl_FragCoord.xy - pc.destination.xy) / outSize;
            vec3 x = cubicAxis(uv.x, size.x, outSize.x, pc.source.x);
            vec3 y = cubicAxis(uv.y, size.y, outSize.y, pc.source.y);
            bool cubicX = 2.0 * size.x < outSize.x;
            bool cubicY = 2.0 * size.y < outSize.y;
            vec4 a = tap(vec2(x.x, y.x));
            if (cubicX) {
                a = mix(a, tap(vec2(x.y, y.x)), x.z);
            }
            if (cubicY) {
                vec4 b = tap(vec2(x.x, y.y));
                if (cubicX) {
                    b = mix(b, tap(vec2(x.y, y.y)), x.z);
                }
                a = mix(a, b, y.z);
            }
            color = a;
        }
        """;
}
