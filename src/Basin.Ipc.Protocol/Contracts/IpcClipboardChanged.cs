namespace Basin.Ipc;

/// <summary>
/// The data of the <c>clipboard/changed</c> event.
/// </summary>
/// <param name="Kind"><c>clipboard</c> or <c>primary</c>: the selection that changed.</param>
public readonly record struct IpcClipboardChanged(string Kind);
