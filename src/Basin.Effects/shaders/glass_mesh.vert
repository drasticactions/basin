#version 450
layout(location = 0) in vec2 a_position;
layout(location = 1) in vec2 a_local;
layout(location = 0) out vec2 v_local;
void main() {
    v_local = a_local;
    gl_Position = vec4(a_position, 0.0, 1.0);
}
