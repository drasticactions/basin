namespace Basin.Ipc;

/// <summary>
/// The reply of <c>diag/tree</c>.
/// </summary>
/// <param name="Nodes">Every scene node in the order of the tree walk, with its depth.</param>
public readonly record struct IpcDiagTree(IpcDiagNode[] Nodes);
