namespace Basin.Capabilities;

/// <summary>
/// A chrome toolkit that draws compositor-owned surfaces into buffers. The compositor pumps it on its own loop.
/// </summary>
public interface IUIHost : IDisposable
{
    /// <summary>
    /// Gets the buffer kinds that the host can produce. <see cref="UISurfaceOptions.Target"/> must name one of them.
    /// </summary>
    UITargetKind Produces { get; }

    /// <summary>
    /// Gets the interface that every surface of the host implements. The default, <see cref="IUISurface"/>, adds no contract.
    /// </summary>
    Type SurfaceContract => typeof(IUISurface);

    /// <summary>
    /// Creates a configured surface. The caller owns the surface and must dispose it.
    /// </summary>
    /// <param name="options">The target kind, the logical size and the scale.</param>
    /// <returns>The surface, or <see langword="null"/> when the host cannot make it, for example for a kind that it does not produce.</returns>
    IUISurface? CreateSurface(in UISurfaceOptions options);

    /// <summary>
    /// Gets the delay in milliseconds until the host needs the next <see cref="Pump"/>.
    /// It is <see langword="null"/> when no work is due, and 0 means now.
    /// </summary>
    long? NextDueMillis { get; }

    /// <summary>
    /// Occurs when the host needs a <see cref="Pump"/> soon, for example after the toolkit posted work.
    /// A host must raise it on the compositor thread.
    /// </summary>
    event Action? WakeupRequested;

    /// <summary>
    /// Occurs when the toolkit opens a popup surface. The default implementation never raises it.
    /// </summary>
    event Action<IUISurface>? PopupAppeared
    {
        add
        {
        }

        remove
        {
        }
    }

    /// <summary>
    /// Occurs when the toolkit closes a popup that <see cref="PopupAppeared"/> reported. The default implementation never raises it.
    /// </summary>
    event Action<IUISurface>? PopupDismissed
    {
        add
        {
        }

        remove
        {
        }
    }

    /// <summary>
    /// Runs the work of the toolkit that is due, for example timers, layout and drawing. Call it on the compositor thread.
    /// </summary>
    void Pump();
}
