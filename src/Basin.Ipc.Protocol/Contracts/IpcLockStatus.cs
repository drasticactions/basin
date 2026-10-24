namespace Basin.Ipc;

/// <summary>
/// The reply of <c>lock/status</c> and the data of the <c>lock/changed</c> event.
/// </summary>
/// <param name="Locked"><see langword="true"/> while the session is locked.</param>
public readonly record struct IpcLockStatus(bool Locked);
