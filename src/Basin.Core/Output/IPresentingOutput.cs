namespace Basin;

/// <summary>
/// Represents an output that knows when a frame reached the screen. Presentation feedback and frame statistics read it.
/// </summary>
public interface IPresentingOutput
{
    /// <summary>
    /// Occurs when a frame is on the screen. The arguments are the monotonic time in nanoseconds, the refresh interval in nanoseconds and a sequence counter.
    /// </summary>
    event Action<ulong, uint, ulong>? PresentedOnScreen;

    /// <summary>
    /// Occurs when a committed frame never reached the screen. The default implementation never raises it.
    /// </summary>
    event Action? PresentationDiscarded
    {
        add
        {
        }

        remove
        {
        }
    }
}
