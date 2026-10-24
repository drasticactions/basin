namespace Basin.Audio.PipeWire;

/// <summary>
/// Reads the process facts that <see cref="AudioStreamOwners"/> uses to match a stream to a window. A test can supply its own table.
/// </summary>
public interface IAudioProcessTable
{
    /// <summary>
    /// Finds the parent of a process.
    /// </summary>
    /// <param name="pid">The process to look up.</param>
    /// <returns>The parent process id, or 0 or less when the process is gone or cannot be read.</returns>
    int ParentOf(int pid);

    /// <summary>
    /// Finds the process group of a process.
    /// </summary>
    /// <param name="pid">The process to look up.</param>
    /// <returns>The process group id, or 0 or less when the process cannot be read.</returns>
    int GroupOf(int pid);

    /// <summary>
    /// Finds the Flatpak application that a process runs in.
    /// </summary>
    /// <param name="pid">The process to look up.</param>
    /// <returns>The Flatpak application id, or <see langword="null"/> when the process does not run in a Flatpak instance or cannot be read.</returns>
    string? FlatpakAppOf(int pid);
}
