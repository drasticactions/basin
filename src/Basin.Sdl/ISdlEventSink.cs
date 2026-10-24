using SDL3;

namespace Basin.Sdl;

/// <summary>
/// Receives every SDL event that <see cref="SdlLoop"/> drains, on the SDL main thread.
/// Each sink sees each event. A sink cannot hide an event from the other sinks.
/// </summary>
public interface ISdlEventSink
{
    /// <summary>
    /// Handles one SDL event. The loop catches and logs an exception that this method raises.
    /// </summary>
    /// <param name="e">The event, valid only for the duration of the call.</param>
    /// <returns><see langword="true"/> when the sink acted on the event, so the loop ticks the host on its next iteration.</returns>
    bool Handle(in SDL.Event e);
}
