using Wayland.Server;

namespace Basin.Capabilities;

/// <summary>
/// Tracks user activity and idle inhibitors. Idle protocols read it to decide when the session is idle.
/// </summary>
public interface IIdleSource
{
    /// <summary>
    /// Gets the time in milliseconds since the last user activity. It is 0 while an inhibitor is held.
    /// </summary>
    long IdleMillis { get; }

    /// <summary>
    /// Gets a value indicating whether at least one idle inhibitor is held.
    /// </summary>
    bool IsInhibited { get; }

    /// <summary>
    /// Occurs on each user activity, after the idle time resets.
    /// </summary>
    event Action? Activity;

    /// <summary>
    /// Occurs when an inhibitor is taken or released.
    /// </summary>
    event Action? InhibitionChanged;

    /// <summary>
    /// Records user activity now. It resets the idle time and raises <see cref="Activity"/>.
    /// </summary>
    void NotifyActivity();

    /// <summary>
    /// Takes an idle inhibitor. The session cannot become idle while any inhibitor is held.
    /// </summary>
    /// <returns>A handle that releases the inhibitor when it is disposed. A second dispose has no effect.</returns>
    IDisposable Inhibit();
}
