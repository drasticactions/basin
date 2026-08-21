#version 450
// Unit-quad vertex shader: dst box in pixels, y-down, no flips — the same
// coordinate convention as the GL renderer (buffer row 0 at NDC -1).
layout(push_constant) uniform Push {
    vec4 dst;
    vec4 src;
    vec2 target;
    float alpha;
    float forceOpaque;
    vec4 color;
    mat3 transform;
} pc;
layout(location = 0) out vec2 v_uv;
void main() {
    vec2 corner = vec2(float(gl_VertexIndex & 1), float((gl_VertexIndex >> 1) & 1));
    v_uv = corner;
    vec2 px = pc.dst.xy + corner * pc.dst.zw;
    vec3 tp = pc.transform * vec3(px, 1.0);
    gl_Position = vec4(tp.xy / pc.target * 2.0 - tp.z, 0.0, tp.z);
}
