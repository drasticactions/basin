namespace Basin.Shell.River;

public sealed record RiverWindowManagerOptions
{
    public int TransactionTimeoutMs { get; init; } = Transaction.DefaultTimeoutMs;

    public int UnresponsiveTimeoutMs { get; init; } = 2000;

    public bool DisconnectUnresponsiveManager { get; init; }

    public bool ManageWithoutWindowManager { get; init; } = true;
}
