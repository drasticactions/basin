using Xkb;

namespace Basin.Seat;

public sealed class KeyboardDevice : Capabilities.IInjectedKeyboard
{
    private readonly SeatKeyboard _owner;

    internal KeyboardDevice(SeatKeyboard owner, bool isDefault)
    {
        _owner = owner;
        IsDefault = isDefault;
        OwnSource = isDefault ? null : new XkbKeymapSource();
    }

    internal bool IsDefault { get; }

    internal XkbKeymapSource? OwnSource { get; private set; }

    internal Capabilities.Keymap? File { get; set; }

    internal XkbKeymap? Compiled { get; set; }

    internal XkbState? State { get; set; }

    internal (uint Depressed, uint Latched, uint Locked, uint Group) Modifiers { get; set; }

    public object? Tag { get; set; }

    public bool SetKeymap(ReadOnlySpan<byte> keymapText) => _owner.SetDeviceKeymap(this, keymapText);

    public void Dispose() => _owner.RemoveDevice(this);

    internal void Teardown()
    {
        State?.Dispose();
        State = null;
        File?.Dispose();
        File = null;
        Compiled = null;
        OwnSource?.Dispose();
        OwnSource = null;
    }
}
