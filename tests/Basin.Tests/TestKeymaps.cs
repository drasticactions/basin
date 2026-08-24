namespace Basin.Tests;

internal static class TestKeymaps
{
    public const string ScrollLock = """
xkb_keymap {
  xkb_keycodes {
    minimum = 8;
    maximum = 255;
    <SCLK> = 78;
    <CAPS> = 66;
    <LFSH> = 50;
    indicator 1 = "Scroll Lock";
    indicator 2 = "Caps Lock";
  };
  xkb_types {
    type "ONE_LEVEL" {
      modifiers = none;
      level_name[Level1] = "Any";
    };
  };
  xkb_compatibility {
    interpret Scroll_Lock { action = LockMods(modifiers = Mod3); };
    interpret Caps_Lock { action = LockMods(modifiers = Lock); };
    interpret Shift_L { action = SetMods(modifiers = Shift); };
    indicator "Scroll Lock" { whichModState = locked; modifiers = Mod3; };
    indicator "Caps Lock" { whichModState = locked; modifiers = Lock; };
  };
  xkb_symbols {
    key <SCLK> { [ Scroll_Lock ] };
    key <CAPS> { [ Caps_Lock ] };
    key <LFSH> { [ Shift_L ] };
    modifier_map Shift { <LFSH> };
    modifier_map Lock { <CAPS> };
  };
};
""";
}
