namespace Basin.SoftKeyboard.Avalonia;

/// <summary>
/// Selects where a <see cref="SoftKeyboardCanvas"/> gets its touches.
/// </summary>
public enum SoftKeyboardInput
{
    /// <summary>
    /// The canvas drives the engine from Avalonia pointer events and hit-tests itself.
    /// </summary>
    Pointer,

    /// <summary>
    /// The canvas draws and takes no input. The consumer feeds touches to the engine.
    /// </summary>
    External,
}
