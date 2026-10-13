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
