namespace Basin.Ipc;

[Flags]
public enum IpcMethodTraits
{
    None = 0,
    ReadOnly = 1,
    Destructive = 2,
    Idempotent = 4,
}
