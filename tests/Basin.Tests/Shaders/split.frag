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
    float split;
};
layout(location = 0) in vec2 v_uv;
layout(location = 0) out vec4 color;
vec3 srgb_to_linear(vec3 c) {
    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}
vec3 linear_to_srgb(vec3 c) {
    return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(0.0031308, c));
}
void main() {
    vec4 c = texture(u_texture, pc.src.xy + v_uv * pc.src.zw);
    c.a = mix(c.a, 1.0, pc.forceOpaque);
    vec3 straight = c.a > 0.0 ? c.rgb / c.a : c.rgb;
    vec3 disp = TEXTURE_TRANSFORM == 1
        ? clamp(straight, 0.0, 1.0)
        : linear_to_srgb(clamp(straight, 0.0, 1.0));
    float g = dot(disp, vec3(0.299, 0.587, 0.114));
    float mask = step(split, v_uv.x);
    vec3 outDisp = mix(disp, vec3(g), mask);
    color = vec4(srgb_to_linear(outDisp) * c.a, c.a) * u_alpha;
}
