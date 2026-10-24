namespace Basin.Ipc;

/// <summary>
/// Asks a person to allow or deny a held call.
/// </summary>
public interface IApprovalBroker
{
    /// <summary>
    /// Starts an approval request for a held call. The broker allows or denies the call when the request ends.
    /// </summary>
    /// <param name="call">The call that the interceptor held.</param>
    /// <param name="reason">A sentence that tells the person why the approval is necessary.</param>
    /// <returns>The request, which records the answer.</returns>
    IpcApproval Request(IpcHeldCall call, string reason);

    /// <summary>
    /// Tells whether a person allowed a method for the rest of the run.
    /// </summary>
    /// <param name="method">The method name.</param>
    /// <returns><see langword="true"/> when an earlier answer allowed the method for the run.</returns>
    bool IsAllowedForRun(string method);
}
