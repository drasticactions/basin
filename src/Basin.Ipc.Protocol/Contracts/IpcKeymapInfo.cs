using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The reply of <c>keyboard/keymap</c> and the data of the <c>keyboard/keymap-changed</c> event.
/// </summary>
/// <param name="Names">The layout names. The library writes <see langword="null"/>, because no keymap source reports them.</param>
/// <param name="Size">The size of the keymap text in bytes, or 0 when the session has no keymap.</param>
/// <param name="Text">The XKB keymap text. It is present only when the request set <see cref="IpcKeymapParams.Text"/>.</param>
/// <param name="Fd">The index of the keymap memfd among the descriptors of the reply. It is present only when the request set <see cref="IpcKeymapParams.Fd"/>.</param>
public readonly record struct IpcKeymapInfo(
    IReadOnlyList<string>? Names,
    uint Size,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Fd = null);
