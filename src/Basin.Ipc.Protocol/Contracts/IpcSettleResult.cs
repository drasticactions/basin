namespace Basin.Ipc;

/// <summary>
/// The reply of <c>session/settle</c>. It goes out when every output has presented what it was asked to draw.
/// </summary>
/// <param name="Presented">The count of frames that the outputs presented while the method waited. It is 0 when nothing was pending.</param>
public readonly record struct IpcSettleResult(long Presented);
