namespace Basin.Ipc;

public readonly record struct IpcCallOutcome(string? ErrorCode, string? ErrorMessage)
{
    public bool Succeeded => ErrorCode is null;
}
