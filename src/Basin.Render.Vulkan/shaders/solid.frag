#version 450
layout(push_constant) uniform Push {
    vec4 dst;
    vec4 src;
    vec2 target;
    float alpha;
    float forceOpaque;
    vec4 color;
} pc;
layout(location = 0) out vec4 color;
void main() { color = pc.color; }
