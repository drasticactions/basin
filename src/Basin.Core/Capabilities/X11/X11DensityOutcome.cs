namespace Basin.Capabilities;

/// <summary>
/// What a density change did, after its launch rule was written.
/// </summary>
public enum X11DensityOutcome
{
    /// <summary>
    /// The application exited and started again on the new X server.
    /// </summary>
    Relaunched,
    /// <summary>
    /// The rule takes effect the next time the application starts. The compositor did not start it, or the relaunch failed.
    /// </summary>
    AppliesNextStart,
    /// <summary>
    /// The application did not exit in time, a relaunch is already in progress, or the compositor stopped.
    /// </summary>
    StillRunning,
    /// <summary>
    /// The application is on the requested X server already.
    /// </summary>
    Unchanged,
}
