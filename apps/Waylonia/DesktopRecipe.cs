namespace Waylonia;

internal sealed record DesktopRecipe(
    string Name,
    string Command,
    string CurrentDesktop,
    IReadOnlyList<string> Env,
    bool Bus,
    bool Gpu,
    string? Video,
    bool SoftwareFallback);
