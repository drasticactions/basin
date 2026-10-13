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
