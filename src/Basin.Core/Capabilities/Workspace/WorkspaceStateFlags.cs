namespace Basin.Capabilities;

[Flags]
public enum WorkspaceStateFlags
{
    None = 0,
    Active = 1,
    Urgent = 2,
    Hidden = 4,
}
