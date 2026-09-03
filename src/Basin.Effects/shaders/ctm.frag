#version 450
layout(constant_id = 0) const int TEXTURE_TRANSFORM = 0;
layout(push_constant) uniform Push {
    vec4 dst;
    vec4 src;
    vec2 target;
    float alpha;
    float forceOpaque;
} pc;
layout(set = 0, binding = 0) uniform sampler2D u_texture;
layout(std140, set = 1, binding = 0) uniform Basin {
    vec2 u_size;
    float u_alpha;
    float m0;
    float m1;
    float m2;
    float m3;
    float m4;
    float m5;
    float m6;
    float m7;
    float m8;
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
    vec3 mapped = vec3(
        dot(vec3(m0, m1, m2), linear),
        dot(vec3(m3, m4, m5), linear),
        dot(vec3(m6, m7, m8), linear));
    mapped = clamp(mapped, 0.0, 1.0);
    color = vec4(mapped * c.a, c.a) * u_alpha;
}
