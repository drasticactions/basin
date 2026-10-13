namespace Basin.Greetd;

public sealed class LoginUsersOptions
{
    public string PasswdPath { get; init; } = "/etc/passwd";

    public string LoginDefsPath { get; init; } = "/etc/login.defs";

    public string IconDirectory { get; init; } = "/var/lib/AccountsService/icons";

    public uint MinUid { get; init; }

    public uint MaxUid { get; init; }

    public IReadOnlySet<string>? Hide { get; init; }

    public IReadOnlySet<uint>? HomedUids { get; init; }
}
