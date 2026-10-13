namespace Basin.Config;

public sealed record LaunchRule
{
    public string? DesktopId { get; init; }

    public string? Exec { get; init; }

    public X11Density? X11 { get; init; }

    public int Specificity => (DesktopId is not null ? 2 : 0) + (Exec is not null ? 1 : 0);

    public bool Matches(string? desktopId, string? exec)
    {
        if (DesktopId is null && Exec is null)
        {
            return false;
        }

        if (DesktopId is not null && (desktopId is null || !SameDesktopId(DesktopId, desktopId)))
        {
            return false;
        }

        return Exec is null || (exec is not null && string.Equals(Exec, Path.GetFileName(exec), StringComparison.Ordinal));
    }

    public static string NormalizeDesktopId(string desktopId)
    {
        ArgumentNullException.ThrowIfNull(desktopId);
        return desktopId.EndsWith(".desktop", StringComparison.Ordinal) ? desktopId : desktopId + ".desktop";
    }

    private static bool SameDesktopId(string a, string b) =>
        string.Equals(NormalizeDesktopId(a), NormalizeDesktopId(b), StringComparison.Ordinal);
}
