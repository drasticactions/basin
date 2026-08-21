namespace Basin.Capabilities;

public interface ITextInputMethod
{
    bool IsAvailable { get; }

    void Activate(Surface surface);

    void Deactivate(Surface surface);

    void SurroundingText(string text, uint cursor, uint anchor);

    void ContentType(uint hint, uint purpose);

    void CursorRectangle(in Box rect);

    void Commit(uint serial);

    void ForwardKey(uint timeMs, uint keycode, bool pressed);

    void ForwardModifiers(uint depressed, uint latched, uint locked, uint group);

    bool HasKeyboardGrab { get; }

    event Action<PreeditString>? Preedit;

    event Action<string>? CommitString;

    event Action<uint, uint>? DeleteSurroundingText;

    event Action? Done;

    event Action? AvailabilityChanged;
}
