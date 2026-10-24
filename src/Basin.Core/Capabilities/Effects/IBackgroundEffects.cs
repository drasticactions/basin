namespace Basin.Capabilities;

/// <summary>
/// Reports the background effects that the renderer can draw, so a protocol advertises only those.
/// </summary>
public interface IBackgroundEffects
{
    /// <summary>
    /// Gets the effects that are available. <see cref="BackgroundEffects.None"/> means that a client gets no effect.
    /// </summary>
    BackgroundEffects Supported { get; }
}
