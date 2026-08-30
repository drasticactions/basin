using Basin.Config;

namespace TinyComp;

internal sealed class Rule : WindowRule
{
    public FrameStyle? FrameStyle { get; init; }

    public int? CornerRadius { get; init; }

    public bool? Effects { get; init; }

    public bool? Wobbly { get; init; }

    public string? Open { get; init; }

    public string? Close { get; init; }

    public int? Workspace { get; init; }

    public int? X { get; init; }

    public int? Y { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public bool WobblyFor(bool fallback) => Effects == false ? false : Wobbly ?? fallback;

    public string? OpenFor(string? fallback) => Effects == false ? null : Open ?? fallback;

    public string? CloseFor(string? fallback) => Effects == false ? null : Close ?? fallback;
}
