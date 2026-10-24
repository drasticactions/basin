namespace Basin.Shell.Nested;

/// <summary>
/// One workspace as <see cref="NestedShell.SnapshotWorkspaces"/> reports it for a panel.
/// </summary>
/// <param name="Index">The zero-based workspace index.</param>
/// <param name="Name">The configured name, or the one-based number when no name is configured.</param>
public sealed record PanelWorkspaceInfo(int Index, string Name);
