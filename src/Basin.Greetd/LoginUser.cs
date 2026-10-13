namespace Basin.Greetd;

public sealed record LoginUser(string Name, uint Uid, string RealName, string Home, string Shell, string? IconPath)
{
    public string DisplayName => RealName.Length > 0 ? RealName : Name;
}
