namespace Basin.Ipc;

/// <summary>
/// The reply of <c>idle/status</c>.
/// </summary>
/// <param name="IdleMs">The time since the last user activity, in milliseconds. It is 0 while an inhibitor is held.</param>
/// <param name="Inhibited"><see langword="true"/> while at least one inhibitor holds the session awake.</param>
public readonly record struct IpcIdleStatus(long IdleMs, bool Inhibited);
