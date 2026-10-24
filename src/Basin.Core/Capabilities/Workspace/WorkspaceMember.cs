namespace Basin.Capabilities;

/// <summary>
/// One toplevel on a workspace.
/// </summary>
/// <param name="ToplevelId">The id of the toplevel in the <see cref="IToplevelModel"/>.</param>
/// <param name="Geometry">The box of the toplevel, in the coordinates that policy uses for the workspace.</param>
public readonly record struct WorkspaceMember(ulong ToplevelId, Box Geometry);
