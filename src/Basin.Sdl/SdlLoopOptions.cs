namespace Basin.Sdl;

public sealed record SdlLoopOptions
{
    public string? AppName { get; init; }

    public string? VideoDriver { get; init; }
}
