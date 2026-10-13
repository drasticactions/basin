#version 450
layout(push_constant) uniform Push {
    vec4 source;
    vec4 sourcePad;
    vec4 destination;
    vec4 axis;
    vec4 clampBox;
} pc;
layout(set = 0, binding = 0) uniform sampler2D src;
layout(location = 0) out vec4 color;
float weight(float i, float sigma) {
    float x = i / sigma;
    float t = clamp(4.0 - x, 0.0, 1.0);
    return exp(-0.5 * x * x) * t * t * (3.0 - 2.0 * t);
}
vec4 tap(vec2 position) {
    return texture(src, clamp(position, pc.clampBox.xy, pc.clampBox.zw) / pc.sourcePad.zw);
}
void main() {
    vec2 direction = pc.axis.xy;
    float sigma = pc.axis.z;
    int count = int(pc.axis.w);
    vec2 position = gl_FragCoord.xy;
    vec4 sum = tap(position);
    float total = 1.0;
    for (int j = 0; j < 12; j++) {
        if (j >= count) {
            break;
        }
        float i = float(2 * j + 1);
        float a = weight(i, sigma);
        float b = weight(i + 1.0, sigma);
        float w = a + b;
        float offset = w > 0.0 ? i + b / w : i;
        sum += (tap(position - direction * offset) + tap(position + direction * offset)) * w;
        total += 2.0 * w;
    }
    color = sum / total;
}
