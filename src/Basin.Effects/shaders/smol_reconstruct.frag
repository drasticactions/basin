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
