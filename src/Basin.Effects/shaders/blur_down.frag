#version 450
layout(push_constant) uniform Push {
    float srcScale;
    float opacity;
    float intensity;
    float reserved;
    vec2 halfpixelTexels;
    vec2 reserved2;
    vec4 colorMatrix0;
    vec4 colorMatrix1;
    vec4 colorMatrix2;
    vec4 box;
    vec4 cornerRadius;
    vec4 frost;
} pc;
layout(set = 0, binding = 0) uniform sampler2D src;
layout(location = 0) out vec4 color;
void main() {
    vec2 texSize = vec2(textureSize(src, 0));
    vec2 uv = gl_FragCoord.xy * pc.srcScale / texSize;
    vec2 hp = pc.halfpixelTexels / texSize;
    vec4 sum = texture(src, uv) * 4.0;
    sum += texture(src, uv - hp);
    sum += texture(src, uv + hp);
    sum += texture(src, uv + vec2(hp.x, -hp.y));
    sum += texture(src, uv - vec2(hp.x, -hp.y));
    color = sum / 8.0;
}
