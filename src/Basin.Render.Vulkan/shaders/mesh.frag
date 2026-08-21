#version 450
layout(constant_id = 0) const int MESH_MODE = 0;
layout(push_constant) uniform Push {
    vec4 dst;
    vec4 src;
    vec2 target;
    float alpha;
    float forceOpaque;
    vec4 color;
    mat3 transform;
} pc;
layout(set = 0, binding = 0) uniform sampler2D u_texture;
layout(location = 0) in vec2 v_uv;
layout(location = 1) in vec4 v_color;
layout(location = 0) out vec4 color;

vec3 srgb_to_linear(vec3 c) {
    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}

void main() {
    vec4 c = vec4(1.0);
    if (MESH_MODE != 0) {
        c = texture(u_texture, v_uv / pc.src.zw);
        c.a = mix(c.a, 1.0, pc.forceOpaque);
        if (MESH_MODE == 2) {
            vec3 straight = c.a > 0.0 ? c.rgb / c.a : c.rgb;
            c.rgb = srgb_to_linear(straight) * c.a;
        }
    }
    color = c * v_color;
}
