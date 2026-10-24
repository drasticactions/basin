namespace Basin.Capabilities;

/// <summary>
/// Compiles XKB keymaps.
/// </summary>
public interface IKeymapSource
{
    /// <summary>Compiles a keymap from XKB text.</summary>
    /// <param name="keymapText">The keymap in the XKB text format.</param>
    /// <param name="keymap">The compiled keymap. The caller owns it and must dispose it.</param>
    /// <returns><see langword="false"/> when the text does not compile.</returns>
    bool TryCompile(string keymapText, out Keymap keymap);

    /// <summary>Compiles a keymap from XKB rule names.</summary>
    /// <param name="names">The rules, model, layout, variant and options. A <see langword="null"/> part uses the XKB default.</param>
    /// <param name="keymap">The compiled keymap. The caller owns it and must dispose it.</param>
    /// <returns><see langword="false"/> when the names do not compile.</returns>
    bool TryCompile(in KeymapNames names, out Keymap keymap);
}
