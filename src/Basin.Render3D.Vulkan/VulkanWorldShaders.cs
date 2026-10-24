namespace Basin.Render3D.Vulkan;

internal static class VulkanWorldShaders
{
    public const string Compiler = "glslangValidator 16.4.0, -V --target-env vulkan1.1";

    public static byte[] Load(string name)
    {
        using var stream = typeof(VulkanWorldShaders).Assembly.GetManifestResourceStream($"world.{name}.spv")
            ?? throw new InvalidOperationException($"The world shader {name} is not embedded in the assembly.");
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }
}
