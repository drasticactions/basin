namespace Basin.Capabilities;

public readonly record struct FakeInputRequest
{
    public required object Client { get; init; }

    public required string Application { get; init; }

    public required string Reason { get; init; }

    public required uint Pid { get; init; }

    public required uint Uid { get; init; }

    public required uint Gid { get; init; }

    public string? SandboxAppId { get; init; }

    public string? SandboxEngine { get; init; }
}
