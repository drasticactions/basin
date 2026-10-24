// The push block and the draw epilogue that every Vulkan texture shader
// shares. A consumer texture shader includes this file, reads pc, and ends
// with basin_finish so the draw's corners, brightness and saturation apply.
layout(push_constant) uniform Push {
    vec4 dst;
    vec4 src;
    vec2 target;
    float alpha;
    float forceOpaque;
    vec4 color;
    vec4 transform0;
    vec4 transform1;
    vec4 transform2;
    vec4 finish;
} pc;

vec3 basin_srgb_to_linear(vec3 c) {
    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}

vec3 basin_linear_to_srgb(vec3 c) {
    return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(0.0031308, c));
}

// c is premultiplied linear light, uv runs from 0 to 1 across pc.dst.
// finish packs half floats: (radius, left inset), (top inset, right inset),
// (bottom inset, 1 - brightness), (1 - saturation, unused). The insets say
// how far the rounded rectangle reaches past each edge of the drawn box.
vec4 basin_finish(vec4 c, vec2 uv) {
    uvec4 bits = floatBitsToUint(pc.finish);
    vec2 a = unpackHalf2x16(bits.x);
    vec2 b = unpackHalf2x16(bits.y);
    vec2 d = unpackHalf2x16(bits.z);
    vec2 e = unpackHalf2x16(bits.w);
    vec2 dimming = vec2(d.y, e.x);
    if (dimming.x != 0.0 || dimming.y != 0.0) {
        vec3 straight = c.a > 0.0 ? c.rgb / c.a : c.rgb;
        vec3 disp = basin_linear_to_srgb(clamp(straight, 0.0, 1.0));
        float luma = dot(disp, vec3(0.2126, 0.7152, 0.0722));
        disp = mix(vec3(luma), disp, 1.0 - dimming.y) * (1.0 - dimming.x);
        c.rgb = basin_srgb_to_linear(disp) * c.a;
    }
    float radius = a.x;
    if (radius > 0.0) {
        vec2 size = pc.dst.zw;
        vec2 lo = -vec2(a.y, b.x);
        vec2 hi = size + vec2(b.y, d.x);
        vec2 halfSize = (hi - lo) * 0.5;
        vec2 p = abs(uv * size - (lo + halfSize)) - (halfSize - vec2(radius));
        float distance = length(max(p, vec2(0.0))) - radius;
        c *= clamp(0.5 - distance, 0.0, 1.0);
    }
    return c;
}
