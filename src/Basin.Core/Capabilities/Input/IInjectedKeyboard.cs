namespace Basin.Capabilities;

/// <summary>
/// A virtual keyboard device that <see cref="IInputSink.CreateKeyboard"/> creates.
/// It can carry its own keymap. Dispose it to remove the device.
/// </summary>
public interface IInjectedKeyboard : IDisposable
{
    /// <summary>
    /// Compiles an XKB text keymap and uses it for the keys of this device.
    /// </summary>
    /// <param name="keymapText">The keymap text in UTF-8.</param>
    /// <returns><see langword="true"/> when the keymap compiled and is now in use.</returns>
    bool SetKeymap(ReadOnlySpan<byte> keymapText);

    /// <summary>
    /// Gets or sets a value that the creator attaches to the device, for example the client that owns it.
    /// </summary>
    object? Tag { get; set; }
}
