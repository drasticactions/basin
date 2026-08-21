#version 450
layout(location = 0) in vec2 v_uv;
layout(location = 0) out vec4 color;
layout(std140, set = 0, binding = 0) uniform Basin {
    vec2 u_size;
    float u_alpha;
    float time;
    float reveal;
    float seed;
    vec4 rect;
    vec3 tint;
};
vec3 srgb_to_linear(vec3 c) {
    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}
float basin_hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7)) + seed) * 43758.5453);
}
float basin_noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = basin_hash(i);
    float b = basin_hash(i + vec2(1.0, 0.0));
    float c = basin_hash(i + vec2(0.0, 1.0));
    float d = basin_hash(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}
float basin_fbm(vec2 p) {
    float v = 0.0;
    float amp = 0.5;
    for (int i = 0; i < 4; i++) {
        v += amp * basin_noise(p);
        p *= 2.03;
        amp *= 0.5;
    }
    return v;
}
vec4 fire(vec2 uv) {
    float line = rect.y + rect.w * reveal;
    float below = uv.y - line;
    float n = basin_fbm(vec2(uv.x * 8.0, (uv.y * 8.0) - (time * 2.0)));
    float inX = smoothstep(rect.x - 0.06, rect.x, uv.x) * (1.0 - smoothstep(rect.x + rect.z, rect.x + rect.z + 0.06, uv.x));
    float band = 1.0 - smoothstep(0.0, 0.16 + (0.12 * n), abs(below));
    float intensity = band * band * (0.55 + (0.45 * n)) * inX;
    vec3 rgb = (tint * intensity * 1.6) + (vec3(1.0, 0.75, 0.3) * intensity * intensity);
    float alpha = clamp(intensity * 1.2, 0.0, 1.0);
    float scorch = 0.45 * (1.0 - smoothstep(0.0, 0.1, -below)) * step(below, 0.0) * inX;
    return vec4(clamp(rgb, 0.0, 1.0) * alpha, min(alpha + (scorch * (1.0 - alpha)), 1.0));
}
void main() {
    vec4 c = fire(v_uv);
    color = vec4(srgb_to_linear(c.rgb / max(c.a, 0.0001)) * c.a, c.a) * u_alpha;
}
