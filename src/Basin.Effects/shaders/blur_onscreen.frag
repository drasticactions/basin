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
layout(set = 1, binding = 0) uniform sampler2D noiseTex;
layout(set = 2, binding = 0) uniform sampler2D plainTex;
layout(location = 0) out vec4 color;
float basin_rounded_box(vec2 position, vec2 center, vec2 extents, vec4 radius) {
    vec2 p = position - center;
    float r = p.x > 0.0
        ? (p.y < 0.0 ? radius.y : radius.w)
        : (p.y < 0.0 ? radius.x : radius.z);
    vec2 q = abs(p) - extents + vec2(r);
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
}
void main() {
    vec2 texSize = vec2(textureSize(src, 0));
    vec2 uv = gl_FragCoord.xy * pc.srcScale / texSize;
    vec2 hp = pc.halfpixelTexels / texSize;
    vec4 sum = texture(src, uv + vec2(-hp.x * 2.0, 0.0));
    sum += texture(src, uv + vec2(-hp.x, hp.y)) * 2.0;
    sum += texture(src, uv + vec2(0.0, hp.y * 2.0));
    sum += texture(src, uv + vec2(hp.x, hp.y)) * 2.0;
    sum += texture(src, uv + vec2(hp.x * 2.0, 0.0));
    sum += texture(src, uv + vec2(hp.x, -hp.y)) * 2.0;
    sum += texture(src, uv + vec2(0.0, -hp.y * 2.0));
    sum += texture(src, uv + vec2(-hp.x, -hp.y)) * 2.0;
    vec4 blurred = sum / 12.0;
    vec4 base = vec4(mix(blurred.rgb, pc.frost.rgb, pc.frost.a), blurred.a);
    vec3 tinted = vec3(
        dot(base, pc.colorMatrix0),
        dot(base, pc.colorMatrix1),
        dot(base, pc.colorMatrix2)) * pc.intensity;
    float noise = texture(noiseTex, gl_FragCoord.xy / vec2(textureSize(noiseTex, 0))).r;
    vec3 result = tinted + vec3(noise);
    float coverage = pc.opacity;
    if (pc.cornerRadius != vec4(0.0)) {
        float f = basin_rounded_box(gl_FragCoord.xy, pc.box.xy, pc.box.zw, pc.cornerRadius);
        float df = fwidth(f);
        coverage *= 1.0 - clamp(0.5 + f / df, 0.0, 1.0);
    }
    vec3 plain = texture(plainTex, gl_FragCoord.xy / vec2(textureSize(plainTex, 0))).rgb;
    color = vec4(mix(plain, result, coverage), blurred.a);
}
