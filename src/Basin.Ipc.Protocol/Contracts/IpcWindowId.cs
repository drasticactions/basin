namespace Basin.Ipc;

/// <summary>
/// The data of the <c>window/removed</c> and <c>window/focused</c> events.
/// </summary>
/// <param name="Id">The id of the window that went away or took focus.</param>
public readonly record struct IpcWindowId(ulong Id);
