namespace Basin.Ipc;

/// <summary>
/// The data of the <c>idle/inhibit-changed</c> event.
/// </summary>
/// <param name="Inhibited"><see langword="true"/> while at least one inhibitor holds the session awake.</param>
public readonly record struct IpcIdleInhibited(bool Inhibited);
