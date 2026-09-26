namespace Basin.Ipc;

public readonly record struct IpcDecision(IpcDecisionKind Kind, string? Message = null)
{
    public static IpcDecision Allow => new(IpcDecisionKind.Allow);

    public static IpcDecision Defer => new(IpcDecisionKind.Defer);

    public static IpcDecision Deny(string message) => new(IpcDecisionKind.Deny, message);
}
