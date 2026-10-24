namespace Basin.Capabilities;

/// <summary>
/// One node of chrome that has no accessibility tree of its own.
/// </summary>
/// <param name="Role">The role.</param>
/// <param name="Name">The name that a screen reader speaks, or <see langword="null"/>.</param>
/// <param name="Bounds">The box in layout coordinates.</param>
/// <param name="States">The states.</param>
/// <param name="Value">The value as text, for example a slider position or a field's text, or <see langword="null"/>.</param>
/// <param name="Press">Runs on the compositor loop when a screen reader presses the node, or <see langword="null"/> when it cannot be pressed.</param>
public readonly record struct AccessibleNode(AccessibleRole Role, string? Name, Box Bounds, AccessibleStates States = AccessibleStates.None, string? Value = null, Action? Press = null);
