namespace Basin.Freedesktop;

public sealed record DesktopAction(string Key, string Name, string? Icon, string? Exec)
{
    public string[] Argv => ExecLine.Split(Exec ?? "");
}
