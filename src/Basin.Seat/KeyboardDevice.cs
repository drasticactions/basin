using Xkb;

namespace Basin.Seat;

/// <summary>
/// A virtual keyboard of a <see cref="SeatKeyboard"/>, with an optional keymap and repeat rate of its own. <see cref="SeatKeyboard.CreateDevice"/> creates it.
/// </summary>
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

    internal (uint? Num, uint? Caps, uint? Scroll, uint? Compose, uint? Kana) LedIndices { get; set; }

    internal (int Rate, int Delay)? Repeat { get; private set; }

    /// <inheritdoc/>
    public object? Tag { get; set; }

    /// <summary>
    /// Compiles an XKB text keymap for this device. Clients receive it while the device is the active one.
    /// </summary>
    /// <param name="keymapText">The keymap text in UTF-8.</param>
    /// <returns><see langword="false"/> when the text does not compile or the device is removed.</returns>
    public bool SetKeymap(ReadOnlySpan<byte> keymapText) => _owner.SetDeviceKeymap(this, keymapText);

    /// <summary>
    /// Compiles a keymap from XKB rule names for this device. Clients receive it while the device is the active one.
    /// </summary>
    /// <param name="names">The rule names.</param>
    /// <returns><see langword="false"/> when the names do not compile or the device is removed.</returns>
    public bool SetKeymap(in Capabilities.KeymapNames names) => _owner.SetDeviceKeymap(this, names);

    /// <summary>
    /// Drops the keymap of this device, so that its keys use the seat keymap. An active device gives the seat keymap back to clients.
    /// </summary>
    public void ClearKeymap() => _owner.ClearDeviceKeymap(this);

    /// <summary>
    /// Sets a repeat rate for this device. Clients receive it while this device sent the last key.
    /// </summary>
    /// <param name="rate">The repeat rate in keys per second. Zero turns repeat off.</param>
    /// <param name="delay">The delay in milliseconds before repeat starts.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rate"/> or <paramref name="delay"/> is negative.</exception>
    public void SetRepeatInfo(int rate, int delay)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rate);
        ArgumentOutOfRangeException.ThrowIfNegative(delay);
        Repeat = (rate, delay);
        _owner.RefreshRepeat();
    }

    /// <summary>
    /// Drops the repeat rate of this device, so that it uses the seat repeat rate.
    /// </summary>
    public void ClearRepeatInfo()
    {
        Repeat = null;
        _owner.RefreshRepeat();
    }

    /// <summary>
    /// Removes the device from the seat and frees its keymap. An active device gives the seat keymap back to clients.
    /// </summary>
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
