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
    float radius;
};
layout(location = 0) in vec2 v_uv;
layout(location = 0) out vec4 color;
vec3 srgb_to_linear(vec3 c) {
    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}
void main() {
    vec4 c = texture(u_texture, pc.src.xy + v_uv * pc.src.zw);
    c.a = mix(c.a, 1.0, pc.forceOpaque);
    if (TEXTURE_TRANSFORM == 1) {
        vec3 straight = c.a > 0.0 ? c.rgb / c.a : c.rgb;
        c.rgb = srgb_to_linear(clamp(straight, 0.0, 1.0)) * c.a;
    }
    vec2 coord = v_uv * u_size;
    vec2 halfSize = u_size * 0.5;
    vec2 p = abs(coord - halfSize) - (halfSize - vec2(radius));
    float d = length(max(p, vec2(0.0))) - radius;
    float mask = 1.0 - smoothstep(-1.0, 0.0, d);
    color = c * mask * u_alpha;
}
