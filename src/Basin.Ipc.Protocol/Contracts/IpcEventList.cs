namespace Basin.Ipc;

/// <summary>
/// The reply of <c>ipc/events</c>, <c>ipc/subscribe</c> and <c>ipc/unsubscribe</c>.
/// </summary>
/// <param name="Events">The declared names for <c>ipc/events</c>, sorted. For the other two, the names that the call named. It is empty after an unsubscribe from every event.</param>
public readonly record struct IpcEventList(ReadOnlyMemory<string> Events);
