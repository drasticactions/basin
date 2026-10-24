namespace Basin.Ipc;

/// <summary>
/// The reply of <c>process/spawn</c>.
/// </summary>
/// <param name="Pid">The process id of the child. The child leads its own session, so it is also the process group id.</param>
/// <param name="LaunchId">The launch id that <c>process/kill</c> and <c>process/log</c> take.</param>
public readonly record struct IpcSpawnResult(int Pid, long LaunchId = 0);
