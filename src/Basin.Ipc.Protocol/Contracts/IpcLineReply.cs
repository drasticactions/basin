namespace Basin.Ipc;

/// <summary>
/// The reply of a method that answers with report lines, for example a consumer method registered with a line form.
/// </summary>
/// <param name="Lines">The report lines in order, each without a line break.</param>
public readonly record struct IpcLineReply(ReadOnlyMemory<string> Lines);
