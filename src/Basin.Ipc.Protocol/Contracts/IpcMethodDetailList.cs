namespace Basin.Ipc;

/// <summary>
/// The reply of <c>ipc/methods</c> with <c>detail</c> set to <see langword="true"/>.
/// </summary>
/// <param name="Methods">One entry for each method that this compositor registered.</param>
public readonly record struct IpcMethodDetailList(ReadOnlyMemory<IpcMethodDetail> Methods);
