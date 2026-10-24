namespace Basin.Capabilities;

/// <summary>
/// Reports the focused text field of the compositor's own chrome, so that policy can show a soft keyboard.
/// </summary>
public interface IUITextFields
{
    /// <summary>
    /// Gets the purpose and hints of the focused chrome text field, or <see langword="null"/> when no chrome text field has focus.
    /// </summary>
    UITextField? Focused { get; }

    /// <summary>
    /// Occurs when <see cref="Focused"/> changes.
    /// </summary>
    event Action? FocusChanged;

    /// <summary>
    /// Occurs for each touch on chrome, with what the touch hit.
    /// </summary>
    event Action<UITouchTarget>? Touched;
}
