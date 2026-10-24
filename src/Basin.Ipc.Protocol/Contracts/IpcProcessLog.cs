namespace Basin.Ipc;

/// <summary>
/// The reply of <c>process/log</c>. It holds the tail of the log file of one launch.
/// </summary>
/// <param name="LaunchId">The launch id.</param>
/// <param name="Path">The absolute path of the log file.</param>
/// <param name="Lines">The last lines of the file, oldest first, without line breaks. It is empty when the file does not exist yet.</param>
public readonly record struct IpcProcessLog(long LaunchId, string Path, ReadOnlyMemory<string> Lines);
