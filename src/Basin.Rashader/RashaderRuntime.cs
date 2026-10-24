namespace Basin.Rashader;

/// <summary>
/// The shader runtime that librashader prepares a preset for. The values match the librashader C
/// ABI.
/// </summary>
public enum RashaderRuntime : uint
{
    /// <summary>
    /// No runtime. librashader does not specialize the preset.
    /// </summary>
    None = 0,
    /// <summary>
    /// The OpenGL or OpenGL ES runtime.
    /// </summary>
    OpenGl = 1,
    /// <summary>
    /// The Vulkan runtime.
    /// </summary>
    Vulkan = 2,
}
