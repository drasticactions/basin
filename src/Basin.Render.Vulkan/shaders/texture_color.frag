#version 450
layout(push_constant) uniform Push {
    vec4 dst;
    vec4 src;
    vec2 target;
    float alpha;
    float forceOpaque;
    vec4 color;
} pc;
layout(set = 0, binding = 0) uniform sampler2D u_texture;
layout(set = 1, binding = 0) uniform Color {
    vec4 m0;
    vec4 m1;
    vec4 m2;
    vec4 source;
    vec4 output_;
    vec4 tone;
} u;
layout(location = 0) in vec2 v_uv;
layout(location = 0) out vec4 color;

const float PQ_M1 = 2610.0 / 16384.0;
const float PQ_M2 = 2523.0 / 4096.0 * 128.0;
const float PQ_C1 = 3424.0 / 4096.0;
const float PQ_C2 = 2413.0 / 4096.0 * 32.0;
const float PQ_C3 = 2392.0 / 4096.0 * 32.0;
const float HLG_A = 0.17883277;
const float HLG_B = 1.0 - 4.0 * 0.17883277;
const float HLG_C = 0.5 - 0.17883277 * log(4.0 * 0.17883277);

vec3 srgb_to_linear(vec3 c) {
    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}

vec3 compound_inverse(vec3 l) {
    return mix(l * 12.92, 1.055 * pow(l, vec3(1.0 / 2.4)) - 0.055, step(0.0031308, l));
}

vec3 pq_eotf(vec3 s) {
    vec3 p = pow(s, vec3(1.0 / PQ_M2));
    vec3 num = max(p - PQ_C1, vec3(0.0));
    return 10000.0 * pow(num / (PQ_C2 - PQ_C3 * p), vec3(1.0 / PQ_M1));
}

float pq_eotf1(float s) {
    float p = pow(s, 1.0 / PQ_M2);
    float num = max(p - PQ_C1, 0.0);
    return 10000.0 * pow(num / (PQ_C2 - PQ_C3 * p), 1.0 / PQ_M1);
}

vec3 pq_inverse(vec3 nits) {
    vec3 y = pow(clamp(nits / 10000.0, 0.0, 1.0), vec3(PQ_M1));
    return pow((PQ_C1 + PQ_C2 * y) / (1.0 + PQ_C3 * y), vec3(PQ_M2));
}

float pq_inverse1(float nits) {
    float y = pow(clamp(nits / 10000.0, 0.0, 1.0), PQ_M1);
    return pow((PQ_C1 + PQ_C2 * y) / (1.0 + PQ_C3 * y), PQ_M2);
}

vec3 hlg_inverse_oetf(vec3 s) {
    return mix(s * s / 3.0, (exp((s - HLG_C) / HLG_A) + HLG_B) / 12.0, step(0.5, s));
}

vec3 hlg_oetf(vec3 e) {
    return mix(sqrt(3.0 * e), HLG_A * log(max(12.0 * e - HLG_B, 1e-6)) + HLG_C, step(1.0 / 12.0, e));
}

vec3 decode(vec4 tf, vec3 s) {
    int kind = int(tf.x);
    float gamma = tf.y;
    float maxLum = tf.z;
    if (kind == 0) return srgb_to_linear(s) * maxLum;
    if (kind == 1) return pow(s, vec3(gamma)) * maxLum;
    if (kind == 2) return s * maxLum;
    if (kind == 3) return pq_eotf(s);
    return pow(hlg_inverse_oetf(s), vec3(1.2)) * maxLum;
}

vec3 encode(vec4 tf, vec3 nits) {
    int kind = int(tf.x);
    float gamma = tf.y;
    float maxLum = tf.z;
    nits = max(nits, vec3(0.0));
    vec3 relative = min(nits / maxLum, vec3(1.0));
    if (kind == 0) return compound_inverse(relative);
    if (kind == 1) return pow(relative, vec3(1.0 / gamma));
    if (kind == 2) return relative;
    if (kind == 3) return pq_inverse(nits);
    return hlg_oetf(pow(relative, vec3(1.0 / 1.2)));
}

float bt2390(float nits) {
    float sourceMax = u.tone.y;
    float targetMax = u.tone.z;
    float knee = u.tone.w;
    float e1 = min(1.0, pq_inverse1(nits) / sourceMax);
    if (e1 <= knee) return nits;
    float t = (e1 - knee) / (1.0 - knee);
    float t2 = t * t;
    float t3 = t2 * t;
    float e2 = (2.0 * t3 - 3.0 * t2 + 1.0) * knee
        + (t3 - 2.0 * t2 + t) * (1.0 - knee)
        + (-2.0 * t3 + 3.0 * t2) * targetMax;
    return pq_eotf1(e2 * sourceMax);
}

void main() {
    vec4 c = texture(u_texture, pc.src.xy + v_uv * pc.src.zw);
    c.a = mix(c.a, 1.0, pc.forceOpaque);
    vec3 straight = clamp(c.a > 0.0 ? c.rgb / c.a : c.rgb, 0.0, 1.0);
    vec3 nits = decode(u.source, straight) * u.source.w;
    nits = max(vec3(dot(u.m0.xyz, nits), dot(u.m1.xyz, nits), dot(u.m2.xyz, nits)), vec3(0.0));
    if (u.tone.x > 0.5) {
        float peak = max(nits.r, max(nits.g, nits.b));
        if (peak > 0.0) {
            nits *= bt2390(peak) / peak;
        }
    }
    vec3 encoded = clamp(encode(u.output_, nits), 0.0, 1.0);
    c.rgb = srgb_to_linear(encoded) * c.a;
    color = c * pc.alpha;
}
