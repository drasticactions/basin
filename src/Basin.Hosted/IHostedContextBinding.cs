namespace Basin.Hosted;

/// <summary>
/// Makes the GPU context of an offscreen <see cref="SkiaSharp.GRContext"/> current on the calling thread for work outside a frame, and restores the previous context after that work.
/// </summary>
public interface IHostedContextBinding
{
    /// <summary>
    /// Makes the context current and records the context that was current before.
    /// </summary>
    /// <returns><see langword="false"/> when the context cannot become current, for example because it is lost.</returns>
    bool Enter();

    /// <summary>
    /// Makes the context current again that was current before the matching <see cref="Enter"/>.
    /// </summary>
    void Exit();
}
