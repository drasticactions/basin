namespace Basin.Effects;

internal static class BlurMaskShaders
{
    internal const string VulkanHeader = """
        #version 450
        layout(set = 0, binding = 0) uniform sampler2D src;
        layout(location = 0) out vec4 color;
        """;

    internal const string GlHeader = """
        #version 300 es
        precision highp float;
        uniform sampler2D src;
        out vec4 color;
        """;

    internal const string Body = """
        void main() {
            color = texture(src, gl_FragCoord.xy / vec2(textureSize(src, 0)));
        }
        """;
}
