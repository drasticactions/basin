namespace Basin.Capabilities;

public interface IKeymapLookup
{
    bool TryKeycodeForKeysym(uint keysym, out uint keycode, out uint modifiers);

    uint KeysymForKeycode(uint keycode);
}
