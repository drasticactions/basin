namespace Basin.Ipc;

/// <summary>
/// The reply of <c>windows/stack</c> and the data of the <c>stack/changed</c> event.
/// </summary>
/// <param name="Ids">The window ids in stacking order, bottom to top.</param>
public readonly record struct IpcStack(ReadOnlyMemory<ulong> Ids);
