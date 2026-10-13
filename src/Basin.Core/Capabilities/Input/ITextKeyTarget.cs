namespace Basin.Capabilities;

public interface ITextKeyTarget
{
    bool Key(uint timeMs, uint keycode, bool pressed);
}
