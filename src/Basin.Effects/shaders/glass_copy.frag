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
    color = texture(plain, gl_FragCoord.xy / vec2(textureSize(plain, 0)));
}
