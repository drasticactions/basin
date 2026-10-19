#version 450
layout(push_constant) uniform Push {
    vec4 box;
    vec4 clampBox;
    vec4 hole;
    vec4 highlight;
    vec4 shape;
    vec4 light;
    vec4 tint;
    vec4 extra;
} pc;
layout(set = 0, binding = 0) uniform sampler2D src;
layout(set = 1, binding = 0) uniform sampler2D plain;
layout(location = 0) out vec4 color;
void main() {
    float f = pc.extra.z;
    vec2 origin = floor(gl_FragCoord.xy) * f;
    int count = f <= 2.0 ? 1 : int(min(f * 0.5, 8.0));
    float step = f / float(count);
    vec4 sum = vec4(0.0);
    for (int j = 0; j < 8; j++) {
        if (j >= count) {
            break;
        }
        for (int i = 0; i < 8; i++) {
            if (i >= count) {
                break;
            }
            vec2 at = clamp(origin + (vec2(float(i), float(j)) + 0.5) * step, pc.clampBox.xy, pc.clampBox.zw);
            sum += texture(src, at / vec2(textureSize(src, 0)));
        }
    }
    color = sum / float(count * count);
}
