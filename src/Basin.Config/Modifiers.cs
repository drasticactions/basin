namespace Basin.Config;

[Flags]
public enum Modifiers
{
    None = 0,

    Shift = 1,

    Ctrl = 4,

    Mod1 = 8,

    Mod3 = 32,

    Mod4 = 64,

    Mod5 = 128,

    Alt = Mod1,

    Super = Mod4,
}
