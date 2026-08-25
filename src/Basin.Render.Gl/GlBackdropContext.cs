namespace Basin.Render.Gl;

public readonly record struct GlBackdropContext
{
    public required GlDevice Device { get; init; }

    public required uint Backdrop { get; init; }

    public required int TargetWidth { get; init; }

    public required int TargetHeight { get; init; }

    public required Box Bounds { get; init; }

    public object? Key { get; init; }
}
