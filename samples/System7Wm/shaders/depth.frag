#version 450
#extension GL_GOOGLE_include_directive : require
layout(constant_id = 0) const int TEXTURE_TRANSFORM = 0;
#include "basin.glsl"
layout(set = 0, binding = 0) uniform sampler2D u_texture;
layout(std140, set = 1, binding = 0) uniform Basin {
    vec2 u_size;
    float u_alpha;
    float depth;
    float dither;
};
layout(location = 0) in vec2 v_uv;
layout(location = 0) out vec4 color;

vec3 srgb_to_linear(vec3 c) {
    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}

vec3 linear_to_srgb(vec3 c) {
    return mix(c * 12.92, (1.055 * pow(c, vec3(1.0 / 2.4))) - 0.055, step(0.0031308, c));
}

float s7_bayer(vec2 p) {
    vec2 lo = mod(floor(p), 2.0);
    vec2 hi = mod(floor(p * 0.5), 2.0);
    float a = mod(2.0 * lo.x + 3.0 * lo.y, 4.0);
    float b = mod(2.0 * hi.x + 3.0 * hi.y, 4.0);
    return (4.0 * a + b + 0.5) / 16.0;
}

vec4 s7_pick(vec4 best, vec3 c, vec3 candidate) {
    vec3 d = c - candidate;
    float e = dot(d, d);
    return e < best.a ? vec4(candidate, e) : best;
}

vec3 s7_quantize(vec3 c, vec2 p) {
    float t = dither > 0.5 ? s7_bayer(p) : 0.5;
    float l = dot(c, vec3(0.299, 0.587, 0.114));
    if (depth < 1.5) {
        return vec3(l >= t ? 1.0 : 0.0);
    }
    if (depth < 2.5) {
        float k = clamp(floor(l * 3.0 + t), 0.0, 3.0);
        return vec3(k < 0.5 ? 0.0 : k < 1.5 ? 85.0 / 255.0 : k < 2.5 ? 172.0 / 255.0 : 1.0);
    }
    if (depth < 4.5) {
        vec3 x = clamp(c + (t - 0.5) * 0.3, 0.0, 1.0);
        vec4 b = vec4(1.0, 1.0, 1.0, 1000.0);
        b = s7_pick(b, x, vec3(1.0, 1.0, 1.0));
        b = s7_pick(b, x, vec3(252.0, 243.0, 5.0) / 255.0);
        b = s7_pick(b, x, vec3(255.0, 100.0, 2.0) / 255.0);
        b = s7_pick(b, x, vec3(221.0, 8.0, 6.0) / 255.0);
        b = s7_pick(b, x, vec3(242.0, 8.0, 132.0) / 255.0);
        b = s7_pick(b, x, vec3(70.0, 0.0, 165.0) / 255.0);
        b = s7_pick(b, x, vec3(0.0, 0.0, 212.0) / 255.0);
        b = s7_pick(b, x, vec3(2.0, 171.0, 234.0) / 255.0);
        b = s7_pick(b, x, vec3(31.0, 183.0, 20.0) / 255.0);
        b = s7_pick(b, x, vec3(0.0, 100.0, 17.0) / 255.0);
        b = s7_pick(b, x, vec3(86.0, 44.0, 5.0) / 255.0);
        b = s7_pick(b, x, vec3(144.0, 113.0, 58.0) / 255.0);
        b = s7_pick(b, x, vec3(192.0, 192.0, 192.0) / 255.0);
        b = s7_pick(b, x, vec3(128.0, 128.0, 128.0) / 255.0);
        b = s7_pick(b, x, vec3(32.0, 32.0, 32.0) / 255.0);
        b = s7_pick(b, x, vec3(0.0, 0.0, 0.0));
        return b.rgb;
    }
    if (depth < 8.5) {
        vec3 x = clamp(c + (t - 0.5) * 0.2, 0.0, 1.0);
        vec3 cube = floor(x * 5.0 + 0.5) / 5.0;
        vec3 d = x - cube;
        vec4 b = vec4(cube, dot(d, d));
        b = s7_pick(b, x, vec3(floor(dot(x, vec3(1.0 / 3.0)) * 15.0 + 0.5) / 15.0));
        b = s7_pick(b, x, vec3(floor(x.r * 15.0 + 0.5) / 15.0, 0.0, 0.0));
        b = s7_pick(b, x, vec3(0.0, floor(x.g * 15.0 + 0.5) / 15.0, 0.0));
        b = s7_pick(b, x, vec3(0.0, 0.0, floor(x.b * 15.0 + 0.5) / 15.0));
        return b.rgb;
    }
    if (depth < 16.5) {
        return floor(c * 31.0 + 0.5) / 31.0;
    }
    return c;
}

void main() {
    vec4 c = texture(u_texture, pc.src.xy + v_uv * pc.src.zw);
    c.a = mix(c.a, 1.0, pc.forceOpaque);
    vec3 straight = c.a > 0.001 ? c.rgb / c.a : c.rgb;
    straight = clamp(straight, 0.0, 1.0);
    vec3 encoded = TEXTURE_TRANSFORM == 1 ? straight : linear_to_srgb(straight);
    vec3 quantized = s7_quantize(encoded, floor(v_uv * u_size));
    color = basin_finish(vec4(srgb_to_linear(quantized) * c.a, c.a), v_uv) * u_alpha;
}
