namespace Basin.Ipc;

/// <summary>
/// Sees every call to a registered method, from a connection and from the line front. An unknown method never reaches it.
/// </summary>
public interface IIpcInterceptor
{
    /// <summary>
    /// Decides whether a call runs before the method sees it. An exception from this method denies the call.
    /// </summary>
    /// <param name="method">The name of the called method.</param>
    /// <param name="parameters">The raw JSON of the call's params. The span is valid only during the callback.</param>
    /// <param name="context">The call's context, valid only during the callback.</param>
    /// <returns>Allow, deny with a message, or defer after <see cref="IpcCallContext.Hold"/>.</returns>
    IpcDecision Before(string method, ReadOnlySpan<byte> parameters, IpcCallContext context);

    /// <summary>
    /// Reports how a call ended, when its reply is complete. A denied call reaches it too. An exception from this method is logged.
    /// </summary>
    /// <param name="method">The name of the called method.</param>
    /// <param name="outcome">The error code and text, or none for success.</param>
    /// <param name="elapsed">The time from the start of <see cref="Before"/> to the complete reply.</param>
    /// <param name="context">The call's context, valid only during the callback. It has no held call.</param>
    void After(string method, IpcCallOutcome outcome, TimeSpan elapsed, IpcCallContext context);
}
