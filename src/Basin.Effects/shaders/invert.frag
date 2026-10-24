#version 450
#extension GL_GOOGLE_include_directive : require
layout(constant_id = 0) const int TEXTURE_TRANSFORM = 0;
#include "basin.glsl"
layout(set = 0, binding = 0) uniform sampler2D u_texture;
layout(std140, set = 1, binding = 0) uniform Basin {
    vec2 u_size;
    float u_alpha;
};
layout(location = 0) in vec2 v_uv;
layout(location = 0) out vec4 color;
vec3 srgb_to_linear(vec3 c) {
    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}
void main() {
    vec4 c = texture(u_texture, pc.src.xy + v_uv * pc.src.zw);
    c.a = mix(c.a, 1.0, pc.forceOpaque);
    vec3 straight = c.a > 0.001 ? c.rgb / c.a : c.rgb;
    straight = clamp(straight, 0.0, 1.0);
    vec3 linear = TEXTURE_TRANSFORM == 1 ? srgb_to_linear(straight) : straight;
    vec3 gamma = vec3(1.0) - pow(max(linear, vec3(0.0)), vec3(1.0 / 2.2));
    linear = pow(max(gamma, vec3(0.0)), vec3(2.2));
    color = basin_finish(vec4(linear * c.a, c.a), v_uv) * u_alpha;
}
