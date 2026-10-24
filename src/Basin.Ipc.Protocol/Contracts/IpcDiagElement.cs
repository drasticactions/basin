namespace Basin.Ipc;

/// <summary>
/// The reply of <c>diag/element</c>.
/// </summary>
/// <param name="Name">The element name that was asked for.</param>
/// <param name="Box">The bounds of the element in layout coordinates.</param>
public readonly record struct IpcDiagElement(string Name, IpcBox Box);
