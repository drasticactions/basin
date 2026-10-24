namespace Basin.Ipc;

/// <summary>
/// The reply of <c>diag/targets</c>.
/// </summary>
/// <param name="Targets">Every target that a selector can reach.</param>
public readonly record struct IpcDiagTargets(IpcDiagTarget[] Targets);
