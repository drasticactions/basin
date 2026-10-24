namespace Basin.Config;

/// <summary>
/// One key binding. It runs a named action or a command, or it removes a binding when both are <see langword="null"/>.
/// </summary>
/// <param name="Keysym">The xkb keysym of the key.</param>
/// <param name="ModifierMask">The modifiers that must be held.</param>
/// <param name="Action">The name of a compositor action, or <see langword="null"/>.</param>
/// <param name="Command">The command and its arguments, or <see langword="null"/>.</param>
public sealed record Hotkey(uint Keysym, Modifiers ModifierMask, string? Action, string[]? Command)
{
    /// <summary>
    /// Gets a value indicating whether the binding has no action and no command, so it removes a default binding.
    /// </summary>
    public bool Unbinds => Action is null && Command is null;

    /// <summary>
    /// Gets the window states in which the binding applies. The default is <see cref="HotkeyScope.Always"/>.
    /// </summary>
    public HotkeyScope When { get; init; }

    /// <summary>
    /// Tells whether the binding applies for the fullscreen state of the focused window.
    /// </summary>
    /// <param name="focusedFullscreen"><see langword="true"/> when the focused window is fullscreen.</param>
    /// <returns><see langword="true"/> when <see cref="When"/> allows the binding in that state.</returns>
    public bool AppliesTo(bool focusedFullscreen) => When switch
    {
        HotkeyScope.Windowed => !focusedFullscreen,
        HotkeyScope.Fullscreen => focusedFullscreen,
        _ => true,
    };
}
