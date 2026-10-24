using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The reply of <c>windows/set-x11-density</c>. It goes out when the density control finishes, which can take its 5 s grace.
/// </summary>
/// <param name="Outcome">One of <see cref="Relaunched"/>, <see cref="AppliesNextStart"/>, <see cref="StillRunning"/> or <see cref="Unchanged"/>.</param>
/// <param name="Rule">The <c>[[launch]]</c> rule that the control wrote.</param>
/// <param name="Reason">Text that explains the outcome. The key is absent when the control gives none.</param>
/// <param name="LaunchId">The launch id of the relaunched application. The key is absent when nothing was relaunched.</param>
public readonly record struct IpcX11DensityResult(
    string Outcome,
    string Rule,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? LaunchId = null)
{
    /// <summary>
    /// The outcome when the application quit and started again on the target server.
    /// </summary>
    public const string Relaunched = "relaunched";

    /// <summary>
    /// The outcome when the rule takes effect the next time the application starts, because the compositor did not start it or the relaunch failed.
    /// </summary>
    public const string AppliesNextStart = "applies-next-start";

    /// <summary>
    /// The outcome when the application did not quit within the grace, a relaunch is already in progress, or the compositor stopped.
    /// </summary>
    public const string StillRunning = "still-running";

    /// <summary>
    /// The outcome when the window already has the target density.
    /// </summary>
    public const string Unchanged = "unchanged";
}
