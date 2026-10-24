namespace Basin;

/// <summary>
/// The kinds of CPU access that <see cref="IBuffer.BeginDataAccess"/> can grant. Combine the flags to ask for read and write access together.
/// </summary>
[Flags]
public enum BufferDataAccess
{
    /// <summary>The caller reads the pixels.</summary>
    Read = 1,
    /// <summary>The caller writes the pixels. A write access increments <see cref="IBuffer.WriteCount"/>.</summary>
    Write = 2,
}
