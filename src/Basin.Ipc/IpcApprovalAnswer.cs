namespace Basin.Ipc;

public enum IpcApprovalAnswer : byte
{
    AllowOnce,
    AllowRun,
    Deny,
    TimedOut,
    NoAnswerer,
}
