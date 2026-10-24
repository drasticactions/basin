namespace Basin.Ipc;

/// <summary>
/// The reply of <c>diag/planes-state</c>.
/// </summary>
/// <param name="Outputs">One report for each output.</param>
public readonly record struct IpcDiagPlaneState(IpcDiagPlaneReport[] Outputs);
