namespace Basin.Capabilities;

/// <summary>
/// The state flags of a workspace. A value can combine any of the flags. The values match the ext-workspace state enum.
/// </summary>
[Flags]
public enum WorkspaceStateFlags
{
    /// <summary>No state is set.</summary>
    None = 0,
    /// <summary>The workspace is shown.</summary>
    Active = 1,
    /// <summary>The workspace wants attention.</summary>
    Urgent = 2,
    /// <summary>A workspace switcher must not show the workspace.</summary>
    Hidden = 4,
}
