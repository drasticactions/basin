namespace Basin.Capabilities;

public interface IKeymapOverride
{
    IDisposable? OverrideKeysyms(ReadOnlySpan<(uint Keycode, uint Keysym)> keys);
}
