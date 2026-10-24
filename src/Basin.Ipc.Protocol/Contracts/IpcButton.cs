using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// A pointer button. On the wire it is a name or an evdev code, and it is always written as the code.
/// </summary>
/// <param name="Code">The evdev button code, for example 0x110 for <c>BTN_LEFT</c>.</param>
[JsonConverter(typeof(IpcButtonConverter))]
public readonly record struct IpcButton(uint Code)
{
    /// <summary>
    /// Gets the left button, <c>BTN_LEFT</c>.
    /// </summary>
    public static IpcButton Left { get; } = new(0x110);

    /// <summary>
    /// Gets the right button, <c>BTN_RIGHT</c>.
    /// </summary>
    public static IpcButton Right { get; } = new(0x111);

    /// <summary>
    /// Gets the middle button, <c>BTN_MIDDLE</c>.
    /// </summary>
    public static IpcButton Middle { get; } = new(0x112);

    /// <summary>
    /// Parses a button name: <c>left</c>, <c>right</c>, <c>middle</c>, <c>side</c> or <c>extra</c>.
    /// </summary>
    /// <param name="name">The lowercase name.</param>
    /// <param name="button">The button, or code 0 when the name is not known.</param>
    /// <returns><see langword="true"/> when the name is known.</returns>
    public static bool TryParse(string name, out IpcButton button)
    {
        uint? code = name switch { "left" => 0x110, "right" => 0x111, "middle" => 0x112, "side" => 0x113, "extra" => 0x114, _ => null };
        button = new IpcButton(code ?? 0);
        return code is not null;
    }
}
