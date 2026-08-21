namespace Basin.Capabilities;

public interface IKeymapSource
{
    bool TryCompile(string keymapText, out Keymap keymap);

    bool TryCompile(in KeymapNames names, out Keymap keymap);
}
