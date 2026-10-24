namespace Basin.Greetd;

/// <summary>
/// A user that a greeter can list, from one line of the passwd file.
/// </summary>
/// <param name="Name">The login name.</param>
/// <param name="Uid">The numeric user id.</param>
/// <param name="RealName">The first comma field of GECOS, trimmed. It can be empty.</param>
/// <param name="Home">The home directory.</param>
/// <param name="Shell">The login shell.</param>
/// <param name="IconPath">The path of the AccountsService icon when the greeter can read it, else <see langword="null"/>.</param>
public sealed record LoginUser(string Name, uint Uid, string RealName, string Home, string Shell, string? IconPath)
{
    /// <summary>
    /// Gets the name to show: the real name, or the login name when the real name is empty.
    /// </summary>
    public string DisplayName => RealName.Length > 0 ? RealName : Name;
}
