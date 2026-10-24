namespace Basin.Ipc;

/// <summary>
/// Tells a client how a method changes the session. A method can combine several traits.
/// </summary>
[Flags]
public enum IpcMethodTraits
{
    /// <summary>
    /// The method has no special trait.
    /// </summary>
    None = 0,
    /// <summary>
    /// The method does not change the session. A capture that writes a file is still read-only.
    /// </summary>
    ReadOnly = 1,
    /// <summary>
    /// The change loses something that the caller cannot restore.
    /// </summary>
    Destructive = 2,
    /// <summary>
    /// A second identical call changes nothing more.
    /// </summary>
    Idempotent = 4,
}
