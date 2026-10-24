namespace Basin.Portal.Prompts.Skia;

/// <summary>
/// Represents a shown prompt view. Dispose it to hide the view.
/// </summary>
public interface ISkiaPromptSurface : IDisposable
{
    /// <summary>
    /// Paints the view again, for example after a state change that input did not cause.
    /// </summary>
    void Repaint();
}
