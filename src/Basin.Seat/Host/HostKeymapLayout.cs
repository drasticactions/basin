namespace Basin.Seat;

internal static class HostKeymapLayout
{
    internal readonly record struct FixedKey(HostKeyCode Code, string Name, string Type, string Symbols);

    private const string One = "ONE_LEVEL";
    private const string Keypad = "KEYPAD";

    internal static readonly FixedKey[] Keys =
    [
        new(HostKeyCode.Escape, "ESC", One, "Escape"),
        new(HostKeyCode.Backspace, "BKSP", One, "BackSpace"),
        new(HostKeyCode.Tab, "TAB", "TWO_LEVEL", "Tab, ISO_Left_Tab"),
        new(HostKeyCode.Enter, "RTRN", One, "Return"),
        new(HostKeyCode.CapsLock, "CAPS", One, "Caps_Lock"),
        new(HostKeyCode.ShiftLeft, "LFSH", One, "Shift_L"),
        new(HostKeyCode.ShiftRight, "RTSH", One, "Shift_R"),
        new(HostKeyCode.ControlLeft, "LCTL", One, "Control_L"),
        new(HostKeyCode.ControlRight, "RCTL", One, "Control_R"),
        new(HostKeyCode.AltLeft, "LALT", One, "Alt_L"),
        new(HostKeyCode.MetaLeft, "LWIN", One, "Super_L"),
        new(HostKeyCode.MetaRight, "RWIN", One, "Super_R"),
        new(HostKeyCode.ContextMenu, "MENU", One, "Menu"),
        new(HostKeyCode.F1, "FK01", One, "F1"),
        new(HostKeyCode.F2, "FK02", One, "F2"),
        new(HostKeyCode.F3, "FK03", One, "F3"),
        new(HostKeyCode.F4, "FK04", One, "F4"),
        new(HostKeyCode.F5, "FK05", One, "F5"),
        new(HostKeyCode.F6, "FK06", One, "F6"),
        new(HostKeyCode.F7, "FK07", One, "F7"),
        new(HostKeyCode.F8, "FK08", One, "F8"),
        new(HostKeyCode.F9, "FK09", One, "F9"),
        new(HostKeyCode.F10, "FK10", One, "F10"),
        new(HostKeyCode.F11, "FK11", One, "F11"),
        new(HostKeyCode.F12, "FK12", One, "F12"),
        new(HostKeyCode.PrintScreen, "PRSC", One, "Print"),
        new(HostKeyCode.ScrollLock, "SCLK", One, "Scroll_Lock"),
        new(HostKeyCode.Pause, "PAUS", One, "Pause"),
        new(HostKeyCode.Insert, "INS", One, "Insert"),
        new(HostKeyCode.Delete, "DELE", One, "Delete"),
        new(HostKeyCode.Home, "HOME", One, "Home"),
        new(HostKeyCode.End, "END", One, "End"),
        new(HostKeyCode.PageUp, "PGUP", One, "Prior"),
        new(HostKeyCode.PageDown, "PGDN", One, "Next"),
        new(HostKeyCode.ArrowUp, "UP", One, "Up"),
        new(HostKeyCode.ArrowDown, "DOWN", One, "Down"),
        new(HostKeyCode.ArrowLeft, "LEFT", One, "Left"),
        new(HostKeyCode.ArrowRight, "RGHT", One, "Right"),
        new(HostKeyCode.NumLock, "NMLK", One, "Num_Lock"),
        new(HostKeyCode.NumpadDivide, "KPDV", One, "KP_Divide"),
        new(HostKeyCode.NumpadMultiply, "KPMU", One, "KP_Multiply"),
        new(HostKeyCode.NumpadSubtract, "KPSU", One, "KP_Subtract"),
        new(HostKeyCode.NumpadAdd, "KPAD", One, "KP_Add"),
        new(HostKeyCode.NumpadEnter, "KPEN", One, "KP_Enter"),
        new(HostKeyCode.Numpad0, "KP0", Keypad, "KP_Insert, KP_0"),
        new(HostKeyCode.Numpad1, "KP1", Keypad, "KP_End, KP_1"),
        new(HostKeyCode.Numpad2, "KP2", Keypad, "KP_Down, KP_2"),
        new(HostKeyCode.Numpad3, "KP3", Keypad, "KP_Next, KP_3"),
        new(HostKeyCode.Numpad4, "KP4", Keypad, "KP_Left, KP_4"),
        new(HostKeyCode.Numpad5, "KP5", Keypad, "KP_Begin, KP_5"),
        new(HostKeyCode.Numpad6, "KP6", Keypad, "KP_Right, KP_6"),
        new(HostKeyCode.Numpad7, "KP7", Keypad, "KP_Home, KP_7"),
        new(HostKeyCode.Numpad8, "KP8", Keypad, "KP_Up, KP_8"),
        new(HostKeyCode.Numpad9, "KP9", Keypad, "KP_Prior, KP_9"),
        new(HostKeyCode.NumpadDecimal, "KPDL", Keypad, "KP_Delete, KP_Decimal"),
    ];

    internal static bool TryName(HostKeyCode code, out FixedKey key)
    {
        foreach (var candidate in Keys)
        {
            if (candidate.Code == code)
            {
                key = candidate;
                return true;
            }
        }

        key = default;
        return false;
    }

    internal const string Preamble = """
            xkb_types {
                virtual_modifiers NumLock,Alt,LevelThree;
                type "ONE_LEVEL" {
                    modifiers = none;
                    level_name[Level1] = "Any";
                };
                type "TWO_LEVEL" {
                    modifiers = Shift;
                    map[Shift] = Level2;
                    level_name[Level1] = "Base";
                    level_name[Level2] = "Shift";
                };
                type "ALPHABETIC" {
                    modifiers = Shift+Lock;
                    map[Shift] = Level2;
                    map[Lock] = Level2;
                    level_name[Level1] = "Base";
                    level_name[Level2] = "Caps";
                };
                type "KEYPAD" {
                    modifiers = Shift+NumLock;
                    map[None] = Level1;
                    map[Shift] = Level2;
                    map[NumLock] = Level2;
                    map[Shift+NumLock] = Level1;
                    level_name[Level1] = "Base";
                    level_name[Level2] = "Number";
                };
                type "FOUR_LEVEL" {
                    modifiers = Shift+LevelThree;
                    map[None] = Level1;
                    map[Shift] = Level2;
                    map[LevelThree] = Level3;
                    map[Shift+LevelThree] = Level4;
                    level_name[Level1] = "Base";
                    level_name[Level2] = "Shift";
                    level_name[Level3] = "Alt Base";
                    level_name[Level4] = "Shift Alt";
                };
                type "FOUR_LEVEL_SEMIALPHABETIC" {
                    modifiers = Shift+Lock+LevelThree;
                    map[None] = Level1;
                    map[Shift] = Level2;
                    map[Lock] = Level2;
                    map[LevelThree] = Level3;
                    map[Shift+LevelThree] = Level4;
                    map[Lock+LevelThree] = Level3;
                    map[Lock+Shift+LevelThree] = Level4;
                    level_name[Level1] = "Base";
                    level_name[Level2] = "Shift";
                    level_name[Level3] = "Alt Base";
                    level_name[Level4] = "Shift Alt";
                };
            };
            xkb_compat {
                virtual_modifiers NumLock,Alt,LevelThree;
                interpret.repeat = false;
                interpret Shift_L { action = SetMods(modifiers = Shift); };
                interpret Shift_R { action = SetMods(modifiers = Shift); };
                interpret Control_L { action = SetMods(modifiers = Control); };
                interpret Control_R { action = SetMods(modifiers = Control); };
                interpret Alt_L { virtualModifier = Alt; action = SetMods(modifiers = Mod1); };
                interpret Alt_R { virtualModifier = Alt; action = SetMods(modifiers = Mod1); };
                interpret Super_L { action = SetMods(modifiers = Mod4); };
                interpret Super_R { action = SetMods(modifiers = Mod4); };
                interpret Caps_Lock { action = LockMods(modifiers = Lock); };
                interpret Num_Lock {
                    virtualModifier = NumLock;
                    useModMapMods = level1;
                    action = LockMods(modifiers = NumLock);
                };
                interpret ISO_Level3_Shift {
                    virtualModifier = LevelThree;
                    useModMapMods = level1;
                    action = SetMods(modifiers = LevelThree);
                };
                interpret Any + Any { action = SetMods(modifiers = modMapMods); };
            };
        """;
}
