namespace Basin.Capabilities;

/// <summary>
/// Blacks out every output for privacy while input and audio continue. Captures and screencasts are black too,
/// except during a see-through pass that a person allowed.
/// </summary>
public interface IScreenCurtain
{
    /// <summary>
    /// Gets a value indicating whether the curtain is drawn.
    /// </summary>
    bool IsDrawn { get; }

    /// <summary>
    /// Gets a value indicating whether a capture now sees the screen. It is <see langword="true"/> while the curtain is lifted or a pass is open.
    /// </summary>
    bool CapturesSee { get; }

    /// <summary>
    /// Occurs after the curtain is drawn or lifted.
    /// </summary>
    event Action? Changed;

    /// <summary>
    /// Draws or lifts the curtain.
    /// </summary>
    /// <param name="drawn"><see langword="true"/> to black out the outputs.</param>
    void Set(bool drawn);

    /// <summary>
    /// Lets captures see the screen until the matching <see cref="EndPass"/>. The outputs stay black. Passes nest.
    /// </summary>
    void BeginPass();

    /// <summary>
    /// Ends a pass that <see cref="BeginPass"/> started. A call without an open pass does nothing.
    /// </summary>
    void EndPass();
}
