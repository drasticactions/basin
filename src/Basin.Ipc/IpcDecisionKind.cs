namespace Basin.Ipc;

/// <summary>
/// Names the three answers that <see cref="IIpcInterceptor.Before"/> can give to a call.
/// </summary>
public enum IpcDecisionKind : byte
{
    /// <summary>
    /// The method runs at once.
    /// </summary>
    Allow,
    /// <summary>
    /// The call answers <c>refused</c> and the method does not run.
    /// </summary>
    Deny,
    /// <summary>
    /// The interceptor held the call through <see cref="IpcCallContext.Hold"/> and answers it later.
    /// </summary>
    Defer,
}
