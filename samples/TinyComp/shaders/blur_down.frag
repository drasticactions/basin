#version 450
layout(push_constant) uniform Push {
    vec2 srcScale;
    vec2 srcInvSize;
    vec2 halfpixel;
} pc;
layout(set = 0, binding = 0) uniform sampler2D src;
layout(location = 0) out vec4 color;
void main() {
    vec2 uv = gl_FragCoord.xy * pc.srcScale * pc.srcInvSize;
    vec2 hp = pc.halfpixel;
    vec4 sum = texture(src, uv) * 4.0;
    sum += texture(src, uv - hp);
    sum += texture(src, uv + hp);
    sum += texture(src, uv + vec2(hp.x, -hp.y));
    sum += texture(src, uv - vec2(hp.x, -hp.y));
    color = sum / 8.0;
}
