using Basin.Diagnostics;

namespace Basin.Renderers;

/// <summary>
/// Describes one step that <see cref="RendererCatalog.CreateWithFallback"/> took from one renderer to another.
/// </summary>
/// <param name="From">The renderer that failed to start.</param>
/// <param name="To">The renderer that the catalog tries next.</param>
/// <param name="Reason">The error message of the failed renderer, or <see langword="null"/> when no render node was given.</param>
public readonly record struct RendererFallback(string From, string To, string? Reason)
{
    /// <summary>
    /// Gets a value indicating whether the step happened because no render node was given, not because a renderer failed.
    /// </summary>
    public bool NoRenderNode => Reason is null;

    /// <summary>
    /// Gets the exception that the failed renderer threw, or <see langword="null"/> when no render node was given.
    /// </summary>
    public Exception? Error { get; init; }

    /// <summary>
    /// Makes a one-line message about the step for a log.
    /// </summary>
    /// <returns>The message.</returns>
    public string Describe() => Reason is null
        ? $"renderer: no render node found, {From} falls back to software rendering"
        : $"renderer: {From} renderer unavailable, falling back to {To}: {Reason}";

    /// <summary>
    /// Writes the step to a log as a warning. The line carries the exception of the failed renderer when there is one.
    /// </summary>
    /// <param name="log">The log to write to.</param>
    public void Log(BasinLogger log)
    {
        if (Error is { } error)
        {
            log.Warn(error, $"renderer: {From} renderer unavailable, falling back to {To}");
            return;
        }

        log.Warn($"{Describe()}");
    }
}
