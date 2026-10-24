namespace TabWm;

internal sealed record LauncherItem(
    string Id,
    string Name,
    string? GenericName,
    string? Icon,
    IReadOnlyList<string> Keywords,
    string ExecName,
    string Category);
