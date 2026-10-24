namespace Basin.Effects;

/// <summary>
/// Selects how a canvas grid draws the glow around its lines.
/// </summary>
public enum GridGlowPath
{
    /// <summary>
    /// Uses the shader glow when the renderer compiles it, and the halo mesh otherwise.
    /// </summary>
    Auto,
    /// <summary>
    /// Uses only the analytic glow shader. No glow draws when the renderer cannot compile it.
    /// </summary>
    Shader,
    /// <summary>
    /// Uses only the additive halo mesh beside each line, which every renderer can draw.
    /// </summary>
    Halo,
    /// <summary>
    /// Draws no glow.
    /// </summary>
    None,
}
