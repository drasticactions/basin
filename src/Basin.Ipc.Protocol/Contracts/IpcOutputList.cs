namespace Basin.Ipc;

/// <summary>
/// The reply of <c>outputs/list</c>.
/// </summary>
/// <param name="Outputs">One entry for each output, enabled or not.</param>
/// <param name="PointerOutput">The name of the output that holds the pointer, or <see langword="null"/> when no output is known to hold it.</param>
public readonly record struct IpcOutputList(ReadOnlyMemory<IpcOutput> Outputs, string? PointerOutput);
