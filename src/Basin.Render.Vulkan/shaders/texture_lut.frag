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

vec3 srgb_to_linear(vec3 c) {
    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}

void main() {
    vec4 c = texture(u_texture, pc.src.xy + v_uv * pc.src.zw);
    c.a = mix(c.a, 1.0, pc.forceOpaque);
    vec3 straight = c.a > 0.0 ? c.rgb / c.a : c.rgb;
    float n = float(textureSize(u_lut, 0).x);
    vec3 coord = clamp(straight, 0.0, 1.0) * ((n - 1.0) / n) + 0.5 / n;
    vec3 looked = texture(u_lut, coord).rgb;
    c.rgb = srgb_to_linear(clamp(looked, 0.0, 1.0)) * c.a;
    color = c * pc.alpha;
}
