namespace Basin.Render.Gl;

public readonly struct GlFilterContext
{
    public required GlDevice Device { get; init; }

    public required uint Source { get; init; }

    public required int SourceWidth { get; init; }

    public required int SourceHeight { get; init; }

    public required uint Target { get; init; }

    public required int TargetWidth { get; init; }

    public required int TargetHeight { get; init; }

    public required Box Viewport { get; init; }

    public required FrameFilterOptions Options { get; init; }
}
