namespace Basin.Capabilities;

public interface IInjectedKeyboard : IDisposable
{
    bool SetKeymap(ReadOnlySpan<byte> keymapText);

    object? Tag { get; set; }
}
