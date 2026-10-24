namespace Basin.Ipc;

/// <summary>
/// How a call ended, as <see cref="IIpcInterceptor.After"/> sees it.
/// </summary>
/// <param name="ErrorCode">The error code of the reply, or <see langword="null"/> when the call succeeded.</param>
/// <param name="ErrorMessage">The error text of the reply, or <see langword="null"/> when the call succeeded.</param>
public readonly record struct IpcCallOutcome(string? ErrorCode, string? ErrorMessage)
{
    /// <summary>
    /// Gets a value indicating whether the reply carries no error code.
    /// </summary>
    public bool Succeeded => ErrorCode is null;
}
