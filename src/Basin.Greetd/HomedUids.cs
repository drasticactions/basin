namespace Basin.Greetd;

public sealed record HomedUids(IReadOnlySet<uint> Uids, string? Reason);
