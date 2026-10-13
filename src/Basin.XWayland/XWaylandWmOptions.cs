namespace Basin.XWayland;

public sealed record XWaylandWmOptions
{
    public double Scale { get; init; } = 1.0;

    public IReadOnlyList<int> DpiBuckets { get; init; } = [];

    public string CursorTheme { get; init; } = string.Empty;

    public int CursorSize { get; init; } = 24;

    public bool PublishXSettings { get; init; } = true;
}
