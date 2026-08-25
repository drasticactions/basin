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
    float zoom;
    float grid;
    vec2 translation;
};
layout(location = 0) in vec2 v_uv;
layout(location = 0) out vec4 color;
vec3 srgb_to_linear(vec3 c) {
    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}
vec4 basin_texture(vec2 coord) {
    vec4 c = texture(u_texture, pc.src.xy + (coord / u_size) * pc.src.zw);
    c.a = mix(c.a, 1.0, pc.forceOpaque);
    if (TEXTURE_TRANSFORM == 1) {
        vec3 straight = c.a > 0.0 ? c.rgb / c.a : c.rgb;
        c.rgb = srgb_to_linear(clamp(straight, 0.0, 1.0)) * c.a;
    }
    return c;
}
void main() {
    vec2 coord = v_uv * u_size;
    vec2 srcPos = (coord - translation) / max(zoom, 0.0001);
    if (grid > 0.5) {
        vec2 center = floor(srcPos) + vec2(0.5);
        vec2 away = abs(srcPos - center);
        float edge = smoothstep(0.4, 0.5, max(away.x, away.y));
        color = mix(basin_texture(center), vec4(0.0, 0.0, 0.0, 1.0), edge) * u_alpha;
        return;
    }
    vec2 texel = srcPos - vec2(0.5);
    vec2 base = floor(texel);
    vec2 part = texel - base;
    vec2 sharp = clamp(((part - vec2(0.5)) * max(zoom, 1.0)) + vec2(0.5), 0.0, 1.0);
    color = basin_texture(base + vec2(0.5) + sharp) * u_alpha;
}
