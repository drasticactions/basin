namespace Basin.Capabilities;

/// <summary>
/// What a touch on chrome hit, as <see cref="IUITextFields.Touched"/> reports it.
/// </summary>
public enum UITouchTarget
{
    /// <summary>The touch hit neither the focused field nor an input pane.</summary>
    Elsewhere,
    /// <summary>The touch hit the focused text field.</summary>
    Field,
    /// <summary>The touch hit an <see cref="IInputPaneView"/>, for example a soft keyboard.</summary>
    InputPane,
}
