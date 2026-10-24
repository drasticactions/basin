namespace Basin.Ipc;

/// <summary>
/// The reply of <c>diag/syncobj</c>: the counters of the explicit sync protocol since the global was created.
/// </summary>
/// <param name="ManagedCommits">The buffer commits on surfaces with a sync object, with or without points.</param>
/// <param name="ExplicitCommits">The commits that carried both an acquire and a release point.</param>
/// <param name="ParkedAcquires">The commits that waited on the loop for an unsignaled acquire point.</param>
/// <param name="UnparkedAcquires">The unsignaled acquire points that the loop cannot wait for.</param>
/// <param name="LateAcquires">The acquire points that had no fence to export when the commit applied.</param>
/// <param name="UnexportableAcquires">The acquire points that still had no fence to export after a CPU wait.</param>
/// <param name="DeferredReleases">The release points that waited for outstanding renderer reads.</param>
/// <param name="SupersededReleases">The release points of buffers that a later synchronized commit replaced before they were shown.</param>
/// <param name="DiscardedReleases">The release points of commits that were discarded before they applied, for example with a destroyed surface.</param>
public readonly record struct IpcDiagSyncobj(
    long ManagedCommits,
    long ExplicitCommits,
    long ParkedAcquires,
    long UnparkedAcquires,
    long LateAcquires,
    long UnexportableAcquires,
    long DeferredReleases,
    long SupersededReleases,
    long DiscardedReleases);
