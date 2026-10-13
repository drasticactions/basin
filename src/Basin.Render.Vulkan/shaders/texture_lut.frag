#version 450
// The LUT is baked in the encoded domain (that is what ImportLut receives),
// so this always samples through the UNORM view: apply it to encoded
// straight values, then linearize the result for blending.
layout(push_constant) uniform Push {
    vec4 dst;
    vec4 src;
    vec2 target;
    float alpha;
    float forceOpaque;
    vec4 color;
} pc;
layout(set = 0, binding = 0) uniform sampler2D u_texture;
layout(set = 1, binding = 0) uniform sampler3D u_lut;
layout(location = 0) in vec2 v_uv;
layout(location = 0) out vec4 color;

vec3 lut_fetch(ivec3 p) {
    return texelFetch(u_lut, p, 0).rgb;
}

vec3 lut_tetrahedral(vec3 coord) {
    int n = textureSize(u_lut, 0).x;
    vec3 f = clamp(coord, 0.0, 1.0) * float(n - 1);
    ivec3 base = min(ivec3(f), ivec3(n - 2));
    vec3 t = f - vec3(base);
    vec3 c000 = lut_fetch(base);
    vec3 c111 = lut_fetch(base + ivec3(1));
    if (t.x >= t.y) {
        if (t.y >= t.z) {
            vec3 c100 = lut_fetch(base + ivec3(1, 0, 0));
            vec3 c110 = lut_fetch(base + ivec3(1, 1, 0));
            return c000 + t.x * (c100 - c000) + t.y * (c110 - c100) + t.z * (c111 - c110);
        }
        if (t.x >= t.z) {
            vec3 c100 = lut_fetch(base + ivec3(1, 0, 0));
            vec3 c101 = lut_fetch(base + ivec3(1, 0, 1));
            return c000 + t.x * (c100 - c000) + t.z * (c101 - c100) + t.y * (c111 - c101);
        }
        vec3 c001 = lut_fetch(base + ivec3(0, 0, 1));
        vec3 c101 = lut_fetch(base + ivec3(1, 0, 1));
        return c000 + t.z * (c001 - c000) + t.x * (c101 - c001) + t.y * (c111 - c101);
    }
    if (t.x >= t.z) {
        vec3 c010 = lut_fetch(base + ivec3(0, 1, 0));
        vec3 c110 = lut_fetch(base + ivec3(1, 1, 0));
        return c000 + t.y * (c010 - c000) + t.x * (c110 - c010) + t.z * (c111 - c110);
    }
    if (t.y >= t.z) {
        vec3 c010 = lut_fetch(base + ivec3(0, 1, 0));
        vec3 c011 = lut_fetch(base + ivec3(0, 1, 1));
        return c000 + t.y * (c010 - c000) + t.z * (c011 - c010) + t.x * (c111 - c011);
    }
    vec3 c001 = lut_fetch(base + ivec3(0, 0, 1));
    vec3 c011 = lut_fetch(base + ivec3(0, 1, 1));
    return c000 + t.z * (c001 - c000) + t.y * (c011 - c001) + t.x * (c111 - c011);
}

vec3 pq_inverse(vec3 nits) {
    vec3 y = pow(clamp(nits / 10000.0, 0.0, 1.0), vec3(2610.0 / 16384.0));
    return pow((3424.0 / 4096.0 + 2413.0 / 4096.0 * 32.0 * y) / (1.0 + 2392.0 / 4096.0 * 32.0 * y), vec3(2523.0 / 4096.0 * 128.0));
}

vec3 shape(vec3 straight, float shaperNits) {
    return shaperNits > 0.0 ? pq_inverse(max(straight, vec3(0.0)) * shaperNits) : clamp(straight, 0.0, 1.0);
}

vec3 srgb_to_linear(vec3 c) {
    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}

void main() {
    vec2 uv = pc.src.xy + v_uv * pc.src.zw;
    if (pc.color.x > 1.5) {
        vec2 size = vec2(textureSize(u_texture, 0));
        vec2 extent = max(fwidth(uv * size), vec2(1.0 / 256.0));
        vec2 pixel = uv * size - 0.5;
        vec2 base = floor(pixel);
        vec2 phase = pixel - base;
        vec2 shift = 0.5 + 0.5 * sin(1.5707963 * clamp((phase - 0.5) / min(extent, vec2(0.25)), -1.0, 1.0));
        uv = (base + 0.5 + shift) / size;
    } else if (pc.color.x > 0.5) {
        vec2 size = vec2(textureSize(u_texture, 0));
        uv = (floor(uv * size) + 0.5) / size;
    }
    vec4 c = texture(u_texture, uv);
    c.a = mix(c.a, 1.0, pc.forceOpaque);
    vec3 straight = c.a > 0.0 ? c.rgb / c.a : c.rgb;
    vec3 looked = lut_tetrahedral(shape(straight, pc.color.y));
    c.rgb = srgb_to_linear(clamp(looked, 0.0, 1.0)) * c.a;
    color = c * pc.alpha;
}
