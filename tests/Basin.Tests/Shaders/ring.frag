#version 450
layout(location = 0) in vec2 v_uv;
layout(location = 0) out vec4 color;
layout(std140, set = 0, binding = 0) uniform Basin {
    vec2 u_size;
    float u_alpha;
    vec2 center;
    float radius;
};
vec3 srgb_to_linear(vec3 c) {
    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}
void main() {
    vec2 coord = v_uv * u_size;
    float d = distance(coord, center);
    float disc = 1.0 - smoothstep(radius - 12.0, radius, d);
    vec3 rgb = mix(vec3(0.1, 0.2, 0.8), vec3(0.9, 0.6, 0.1), coord.x / u_size.x);
    color = vec4(srgb_to_linear(rgb) * disc, disc) * u_alpha;
}
