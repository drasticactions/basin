namespace Basin.Desktop;

/// <summary>
/// Tells the consumer which size a window gets when policy floats it.
/// </summary>
public enum FloatSize
{
    /// <summary>The consumer chooses the floating size.</summary>
    Policy,

    /// <summary>The window keeps its current size. The window was fullscreen, and a windowed size would make the client resize twice.</summary>
    Keep,
}
