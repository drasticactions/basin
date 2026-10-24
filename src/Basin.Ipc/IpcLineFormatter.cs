namespace Basin.Ipc;

/// <summary>
/// Turns a finished reply into the text that the stdin line front prints.
/// </summary>
/// <param name="reply">The finished reply.</param>
/// <returns>The text to print, which can hold several lines, or <see langword="null"/> to print nothing.</returns>
public delegate string? IpcLineFormatter(IpcReply reply);
