#version 450
// Sampled content enters the blend in linear light. TEXTURE_TRANSFORM says
// how it gets there: 0 = the sampled value is already linear (an sRGB view
// decoded it in hardware, or the content is linear to begin with); 1 = the
// content is sRGB-encoded and the view could not decode it, so decode here —
// on straight alpha, because the encoding applies to color, not coverage.
layout(constant_id = 0) const int TEXTURE_TRANSFORM = 0;
layout(push_constant) uniform Push {
    vec4 dst;
    vec4 src;
    vec2 target;
    float alpha;
    float forceOpaque;
    vec4 color;
} pc;
layout(set = 0, binding = 0) uniform sampler2D u_texture;
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
    color = c * pc.alpha;
}
