namespace Basin.AppMenu;

/// <summary>
/// The modifier keys of an <see cref="AppMenuShortcut"/>. The values are flags, and a combination means that all of its keys are held.
/// </summary>
[Flags]
public enum AppMenuModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary>The Shift key.</summary>
    Shift = 1,

    /// <summary>The Control key. GTK writes it as <c>Primary</c>.</summary>
    Control = 2,

    /// <summary>The Alt key, also called Mod1.</summary>
    Alt = 4,

    /// <summary>The Super key, also called Meta, Mod4 or Logo.</summary>
    Super = 8,
}
