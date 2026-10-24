namespace Basin.Ipc;

/// <summary>
/// What <see cref="IpcEventWait.RunAsync"/> collected.
/// </summary>
/// <param name="Events">The events that matched, in arrival order. It is empty after a timeout with no match.</param>
/// <param name="Stopped"><see langword="true"/> when the caller's action returned <see langword="false"/> and the wait ended before it read an event.</param>
public sealed record IpcEventWaitResult(IReadOnlyList<IpcEvent> Events, bool Stopped);
