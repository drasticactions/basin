namespace Basin.Ipc;

/// <summary>
/// The reply of <c>ipc/methods</c> without <c>detail</c>.
/// </summary>
/// <param name="Methods">The names of the registered methods, sorted. An omitted method or a method with no capability is not in the list.</param>
public readonly record struct IpcMethodList(ReadOnlyMemory<string> Methods);
