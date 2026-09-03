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
    float mode;
    float intensity;
};
layout(location = 0) in vec2 v_uv;
layout(location = 0) out vec4 color;
vec3 srgb_to_linear(vec3 c) {
    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}
void main() {
    vec4 c = texture(u_texture, pc.src.xy + v_uv * pc.src.zw);
    c.a = mix(c.a, 1.0, pc.forceOpaque);
    vec3 straight = c.a > 0.001 ? c.rgb / c.a : c.rgb;
    straight = clamp(straight, 0.0, 1.0);
    vec3 linear = TEXTURE_TRANSFORM == 1 ? srgb_to_linear(straight) : straight;
    if (mode >= 2.5) {
        float luma = dot(linear, vec3(0.2126, 0.7152, 0.0722));
        linear = mix(linear, vec3(luma), clamp(intensity, 0.0, 1.0));
    } else {
        mat3 srgbToLMS = mat3(
            17.8824, 3.45565, 0.0299566,
            43.5161, 27.1554, 0.184309,
            4.11935, 3.86714, 1.46709);
        mat3 errorMat = mat3(
            0.0809444479, -0.0102485335, -0.000365296938,
            -0.130504409, 0.0540193266, -0.00412161469,
            0.116721066, -0.113614708, 0.693511405);
        mat3 defect = mat3(0.0, 0.0, 0.0, 2.02344, 1.0, 0.0, -2.52581, 0.0, 1.0);
        if (mode >= 1.5) {
            defect = mat3(1.0, 0.0, -0.395913, 0.0, 1.0, 0.801109, 0.0, 0.0, 0.0);
        } else if (mode >= 0.5) {
            defect = mat3(1.0, 0.494207, 0.0, 0.0, 0.0, 0.0, 0.0, 1.24827, 1.0);
        }
        vec3 lms = defect * (srgbToLMS * linear);
        vec3 err = errorMat * lms;
        vec3 diff = (linear - err) * intensity;
        vec3 correction = vec3(0.0, (diff.r * 0.7) + diff.g, (diff.r * 0.7) + diff.b);
        linear = linear + correction;
    }
    color = vec4(clamp(linear, 0.0, 1.0) * c.a, c.a) * u_alpha;
}
