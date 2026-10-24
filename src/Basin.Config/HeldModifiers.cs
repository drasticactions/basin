using Xkb;

namespace Basin.Config;

/// <summary>
/// Converts the active modifiers of an xkb state to <see cref="Modifiers"/>.
/// </summary>
public static class HeldModifiers
{
    /// <summary>
    /// Reads the Shift, Control, Mod1, Mod3, Mod4 and Mod5 modifiers that are active in the state. Lock and Mod2 are not reported.
    /// </summary>
    /// <param name="state">The xkb state, or <see langword="null"/>.</param>
    /// <returns>The active modifiers, or <see cref="Modifiers.None"/> when <paramref name="state"/> is <see langword="null"/>.</returns>
    public static Modifiers From(XkbState? state)
    {
        if (state is null)
        {
            return Modifiers.None;
        }

        var held = Modifiers.None;
        if (state.IsModActive("Shift"))
        {
            held |= Modifiers.Shift;
        }

        if (state.IsModActive("Control"))
        {
            held |= Modifiers.Ctrl;
        }

        if (state.IsModActive("Mod1"))
        {
            held |= Modifiers.Mod1;
        }

        if (state.IsModActive("Mod3"))
        {
            held |= Modifiers.Mod3;
        }

        if (state.IsModActive("Mod4"))
        {
            held |= Modifiers.Mod4;
        }

        if (state.IsModActive("Mod5"))
        {
            held |= Modifiers.Mod5;
        }

        return held;
    }
}
