namespace Basin.Capabilities;

/// <summary>
/// Reads the counters of the explicit sync protocol, so that a run can show which paths a client took.
/// </summary>
public interface IExplicitSyncDiagnostics
{
    /// <summary>Gets the counters as of now.</summary>
    ExplicitSyncCounters Counters { get; }
}
