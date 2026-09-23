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
vec2 g_lo;
vec2 g_hi;
vec4 tap(sampler2D s, vec2 uv) { return texture(s, clamp(uv, g_lo, g_hi)); }
void main() {
    vec2 texSize = vec2(textureSize(src, 0));
    vec2 uv = gl_FragCoord.xy * pc.srcScale / texSize;
    vec2 hp = pc.halfpixelTexels / texSize;
    g_lo = (pc.box.xy - pc.box.zw) / (texSize * pc.reserved) + (0.5 / texSize);
    g_hi = (pc.box.xy + pc.box.zw) / (texSize * pc.reserved) - (0.5 / texSize);
    vec4 sum = tap(src, uv) * 4.0;
    sum += tap(src, uv - hp);
    sum += tap(src, uv + hp);
    sum += tap(src, uv + vec2(hp.x, -hp.y));
    sum += tap(src, uv - vec2(hp.x, -hp.y));
    color = sum / 8.0;
}
