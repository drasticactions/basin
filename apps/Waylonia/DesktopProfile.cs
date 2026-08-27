namespace Waylonia;

internal sealed record DesktopProfile(
    string Name,
    string? Recipe,
    string? Host,
    string? Size,
    string? Command,
    IReadOnlyList<string> Env,
    bool? Gpu,
    string? Video);
