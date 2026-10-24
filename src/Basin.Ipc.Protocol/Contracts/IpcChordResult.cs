namespace Basin.Ipc;

/// <summary>
/// The reply of a chord method.
/// </summary>
/// <param name="Codes">The evdev keycodes, in the order they were pressed. They were released in reverse.</param>
public readonly record struct IpcChordResult(ReadOnlyMemory<uint> Codes);
