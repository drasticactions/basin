namespace Basin.Ipc;

/// <summary>
/// The reply of <c>input/text</c> and <c>seat/text</c>.
/// </summary>
/// <param name="Via">The path that delivered the text: <c>text-input</c> or <c>keymap</c>.</param>
public readonly record struct IpcTextResult(string Via);
