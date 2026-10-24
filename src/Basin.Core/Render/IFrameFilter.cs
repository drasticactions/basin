namespace Basin;

/// <summary>
/// Represents a foreign renderer that transforms one whole frame, for example a librashader preset. Each renderer defines the concrete interface it accepts.
/// </summary>
public interface IFrameFilter
{
    /// <summary>
    /// Gets a value indicating whether the filter can run on its device.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Gets a value indicating whether the filter must see the whole frame, which turns off the damage-limited path. The default is <see langword="true"/>.
    /// </summary>
    bool NeedsFullFrame => true;

    /// <summary>
    /// Gets a value indicating whether the output must repaint every frame, for example for an animated preset. The default is <see langword="false"/>.
    /// </summary>
    bool NeedsContinuousRepaint => false;
}
