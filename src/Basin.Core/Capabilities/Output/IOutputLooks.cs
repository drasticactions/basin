namespace Basin.Capabilities;

/// <summary>
/// Applies a look, a 3D color lookup table loaded from a file, to an output.
/// </summary>
public interface IOutputLooks
{
    /// <summary>
    /// Gives the file of the look on an output.
    /// </summary>
    /// <param name="output">The output to read.</param>
    /// <returns>The path that <see cref="SetLook"/> loaded, or <see langword="null"/> when the output has no look.</returns>
    string? LookOf(IOutput output);

    /// <summary>
    /// Loads a look from a file and applies it, or removes the look.
    /// </summary>
    /// <param name="output">The output to change.</param>
    /// <param name="path">The path of a <c>.cube</c> file, or <see langword="null"/> to remove the look.</param>
    /// <param name="error">A readable reason when the file cannot be read or parsed, otherwise <see langword="null"/>.</param>
    /// <returns><see langword="false"/> when the file cannot be read or parsed. The current look then stays.</returns>
    bool SetLook(IOutput output, string? path, out string? error);
}
