namespace Basin.UI.ImGui;

/// <summary>
/// The xkb modifier masks that an <see cref="ImGuiUISurface"/> maps onto the ImGui modifier keys. A mask of 0 makes that ImGui modifier always up.
/// </summary>
/// <param name="Control">The xkb mask that sets the ImGui Ctrl modifier.</param>
/// <param name="Shift">The xkb mask that sets the ImGui Shift modifier.</param>
/// <param name="Alt">The xkb mask that sets the ImGui Alt modifier.</param>
/// <param name="Super">The xkb mask that sets the ImGui Super modifier.</param>
public readonly record struct ImGuiModifierMasks(uint Control, uint Shift, uint Alt, uint Super)
{
    /// <summary>
    /// Gets the masks of the usual xkb keymap. Shift is bit 0, Control is bit 2, Mod1 is bit 3 and Mod4 is bit 6.
    /// </summary>
    public static ImGuiModifierMasks Standard { get; } = new(Control: 1u << 2, Shift: 1u << 0, Alt: 1u << 3, Super: 1u << 6);

    /// <summary>
    /// Makes the masks from xkb modifier indices, for example the values of a keymap lookup.
    /// </summary>
    /// <param name="control">The index of the Control modifier, or <see langword="null"/> when the keymap has none.</param>
    /// <param name="shift">The index of the Shift modifier, or <see langword="null"/> when the keymap has none.</param>
    /// <param name="alt">The index of the Alt modifier, usually Mod1, or <see langword="null"/>.</param>
    /// <param name="super">The index of the Super modifier, usually Mod4, or <see langword="null"/>.</param>
    /// <returns>The masks. An index that is <see langword="null"/> or 32 and more gives a mask of 0.</returns>
    public static ImGuiModifierMasks FromIndices(uint? control, uint? shift, uint? alt, uint? super) =>
        new(Bit(control), Bit(shift), Bit(alt), Bit(super));

    private static uint Bit(uint? index) => index is { } i and < 32 ? 1u << (int)i : 0;
}
