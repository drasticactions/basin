namespace Basin.Renderers;

public readonly record struct RendererFallback(string From, string To, string? Reason)
{
    public bool NoRenderNode => Reason is null;

    public string Describe() => Reason is null
        ? $"{From} requested but no render node was found; using software rendering"
        : $"{From} renderer unavailable ({Reason}); falling back to {To}";
}
