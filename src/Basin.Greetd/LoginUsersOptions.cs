namespace Basin.Greetd;

/// <summary>
/// The options for <see cref="LoginUsers.Read"/> and <see cref="LoginUsers.UidRange"/>.
/// </summary>
public sealed class LoginUsersOptions
{
    /// <summary>
    /// Gets the path of the passwd file to read.
    /// </summary>
    public string PasswdPath { get; init; } = "/etc/passwd";

    /// <summary>
    /// Gets the path of the file that gives <c>UID_MIN</c> and <c>UID_MAX</c>.
    /// </summary>
    public string LoginDefsPath { get; init; } = "/etc/login.defs";

    /// <summary>
    /// Gets the directory that holds one icon file for each user name.
    /// </summary>
    public string IconDirectory { get; init; } = "/var/lib/AccountsService/icons";

    /// <summary>
    /// Gets the lowest UID to list. Zero uses <c>UID_MIN</c> from the login.defs file.
    /// </summary>
    public uint MinUid { get; init; }

    /// <summary>
    /// Gets the highest UID to list. Zero uses <c>UID_MAX</c> from the login.defs file.
    /// </summary>
    public uint MaxUid { get; init; }

    /// <summary>
    /// Gets the login names to leave out of the list, or <see langword="null"/> for none.
    /// </summary>
    public IReadOnlySet<string>? Hide { get; init; }

    /// <summary>
    /// Gets the UIDs to list also when they are outside the UID range, for example the users of systemd-homed.
    /// </summary>
    public IReadOnlySet<uint>? HomedUids { get; init; }
}
