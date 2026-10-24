namespace Basin.Pty;

/// <summary>
/// The way a pseudo-terminal child ended. Exactly one of <see cref="Status"/> and <see cref="Signal"/> has a value.
/// </summary>
/// <param name="Status">The exit code from 0 to 255 when the child called exit, otherwise <see langword="null"/>.</param>
/// <param name="Signal">The number of the signal that killed the child, otherwise <see langword="null"/>.</param>
public readonly record struct PtyExit(int? Status, int? Signal)
{
    /// <summary>
    /// Gets a value indicating whether the child exited normally with code 0. A child that a signal killed did not succeed.
    /// </summary>
    public bool Succeeded => Status == 0;

    internal static PtyExit FromWaitStatus(int status) => (status & 0x7f) == 0
        ? new PtyExit((status >> 8) & 0xff, null)
        : new PtyExit(null, status & 0x7f);
}
