namespace Basin.Capabilities;

/// <summary>
/// Sets a color transformation matrix on an output, in hardware or in a render stage.
/// </summary>
public interface ICtmControl
{
    /// <summary>
    /// Tells whether <see cref="SetCtm"/> can work on an output.
    /// </summary>
    /// <param name="output">The output to check.</param>
    /// <returns><see langword="true"/> when the output can take a matrix.</returns>
    bool SupportsCtm(IOutput output);

    /// <summary>
    /// Applies a 3x3 matrix to the linear RGB color of every pixel on an output.
    /// </summary>
    /// <param name="output">The output to change.</param>
    /// <param name="rowMajor3x3">Nine values in row-major order.</param>
    /// <returns><see langword="true"/> when the matrix is in effect.</returns>
    bool SetCtm(IOutput output, ReadOnlySpan<double> rowMajor3x3);

    /// <summary>
    /// Removes the matrix from an output, so colors pass unchanged.
    /// </summary>
    /// <param name="output">The output to reset.</param>
    /// <returns><see langword="true"/> when the output is back to the identity.</returns>
    bool ResetCtm(IOutput output);
}
