#version 450
// The second subpass of the two-pass pathway: the composite happened in
// linear FP16, this encodes it into the UNORM target. Premultiplied
// throughout — decode and encode act on straight color, coverage passes
// through untouched.
layout(input_attachment_index = 0, set = 0, binding = 0) uniform subpassInput u_blend;
layout(location = 0) out vec4 color;

vec3 linear_to_srgb(vec3 c) {
    return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(0.0031308, c));
}

void main() {
    vec4 c = subpassLoad(u_blend);
    vec3 straight = c.a > 0.0 ? c.rgb / c.a : c.rgb;
    color = vec4(linear_to_srgb(clamp(straight, 0.0, 1.0)) * c.a, c.a);
}
