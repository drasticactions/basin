namespace Basin.Capabilities;

/// <summary>
/// The seam that every frame-paced protocol drives off. The consumer calls <see cref="IFrameSink.BeginFrame"/> and
/// <see cref="IFrameSink.EndFrame"/> from its repaint, and the clock passes each call to every registered sink.
/// </summary>
public interface IFrameClock : IFrameSink
{
    /// <summary>
    /// Registers a sink for the next frames. A sink that is registered already is ignored.
    /// </summary>
    /// <param name="sink">The sink to call on each frame.</param>
    void Add(IFrameSink sink);

    /// <summary>
    /// Unregisters a sink. A sink that is not registered is ignored.
    /// </summary>
    /// <param name="sink">The sink to stop calling.</param>
    void Remove(IFrameSink sink);

    /// <summary>
    /// Occurs when <see cref="RequestFrame"/> asks for a repaint. The default implementation never raises it.
    /// </summary>
    event Action? FrameRequested
    {
        add
        {
        }

        remove
        {
        }
    }

    /// <summary>
    /// Asks the owner of the outputs for a repaint, for example after an animation tick that drew new pixels.
    /// The default implementation does nothing.
    /// </summary>
    void RequestFrame()
    {
    }
}
