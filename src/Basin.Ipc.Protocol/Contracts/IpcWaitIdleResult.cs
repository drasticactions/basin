namespace Basin.Ipc;

/// <summary>
/// The reply of <c>windows/wait-idle</c> when the target goes idle.
/// </summary>
/// <param name="WaitedMs">The time from the request to the answer, in milliseconds.</param>
/// <param name="Commits">The count of commits that restarted the quiet timer.</param>
/// <param name="Ignored">The count of commits that did not count, because their damage was small or empty.</param>
public readonly record struct IpcWaitIdleResult(long WaitedMs, long Commits, long Ignored);
