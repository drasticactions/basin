namespace Basin.Ipc;

/// <summary>
/// The params contract of a library method. Each required key records its presence when it is set, so the server can name a key that is absent.
/// </summary>
public interface IIpcParams
{
    /// <summary>
    /// Gets the error message for the first required key that is absent, or <see langword="null"/> when the params are complete.
    /// An explicit JSON <c>null</c> does not count as present.
    /// </summary>
    string? Missing { get; }
}
