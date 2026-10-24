namespace Basin.Ipc;

/// <summary>
/// Tells how an approval request ended.
/// </summary>
public enum IpcApprovalAnswer : byte
{
    /// <summary>
    /// A person allowed this one call.
    /// </summary>
    AllowOnce,
    /// <summary>
    /// A person allowed the call and every later call to the same method for the run.
    /// </summary>
    AllowRun,
    /// <summary>
    /// A person denied the call, or the compositor stopped.
    /// </summary>
    Deny,
    /// <summary>
    /// No one answered before the timeout, so the call was denied.
    /// </summary>
    TimedOut,
    /// <summary>
    /// No connection and no local handler was able to answer, so the call was denied at once.
    /// </summary>
    NoAnswerer,
}
