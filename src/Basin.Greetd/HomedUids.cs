namespace Basin.Greetd;

/// <summary>
/// The result of <see cref="HomedUsers.ListAsync"/>.
/// </summary>
/// <param name="Uids">The UIDs of the users that systemd-homed manages. It is empty when homed is not reachable.</param>
/// <param name="Reason">Why the set is empty because of a failure, or <see langword="null"/> when the call succeeded.</param>
public sealed record HomedUids(IReadOnlySet<uint> Uids, string? Reason);
