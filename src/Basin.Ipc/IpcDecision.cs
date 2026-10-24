namespace Basin.Ipc;

/// <summary>
/// The answer of an <see cref="IIpcInterceptor"/> to one call, before the method runs.
/// </summary>
/// <param name="Kind">The kind of answer.</param>
/// <param name="Message">The text of the <c>refused</c> error for <see cref="IpcDecisionKind.Deny"/>. Other kinds ignore it.</param>
public readonly record struct IpcDecision(IpcDecisionKind Kind, string? Message = null)
{
    /// <summary>
    /// Gets a decision that lets the method run at once.
    /// </summary>
    public static IpcDecision Allow => new(IpcDecisionKind.Allow);

    /// <summary>
    /// Gets a decision that holds the call. The interceptor must call <see cref="IpcCallContext.Hold"/> before it returns this, or the call answers <c>internal</c>.
    /// </summary>
    public static IpcDecision Defer => new(IpcDecisionKind.Defer);

    /// <summary>
    /// Makes a decision that refuses the call.
    /// </summary>
    /// <param name="message">The text that the <c>refused</c> error carries to the caller.</param>
    /// <returns>A <see cref="IpcDecisionKind.Deny"/> decision.</returns>
    public static IpcDecision Deny(string message) => new(IpcDecisionKind.Deny, message);
}
