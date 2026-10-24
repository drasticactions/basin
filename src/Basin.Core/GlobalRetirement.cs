using Wayland.Server;

namespace Basin;

/// <summary>
/// Removes a Wayland global and destroys it later, so that clients that bind it during the removal do not fail.
/// </summary>
public static class GlobalRetirement
{
    /// <summary>
    /// The default time in milliseconds between the removal of a global and its destruction.
    /// </summary>
    public const int DefaultGraceMillis = 5000;

    /// <summary>
    /// Removes <paramref name="global"/> from the registry now and calls <paramref name="dispose"/> once later. The call happens when the global reports that it is withdrawn, or when the grace time ends. A global that is already removed is ignored.
    /// </summary>
    /// <param name="display">The display whose event loop runs the grace timer.</param>
    /// <param name="global">The global to remove.</param>
    /// <param name="dispose">The callback that destroys the global. It runs on the event loop thread.</param>
    /// <param name="graceMillis">The maximum delay in milliseconds before <paramref name="dispose"/> runs. A value of 0 or less becomes 1.</param>
    public static void Retire(WlServerDisplay display, WlGlobal global, Action dispose, int graceMillis = DefaultGraceMillis)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(global);
        ArgumentNullException.ThrowIfNull(dispose);

        if (global.IsRemoved)
        {
            return;
        }

        var completed = false;
        WlEventSource? timer = null;

        void Complete()
        {
            if (completed)
            {
                return;
            }

            completed = true;
            timer?.Remove();
            dispose();
        }

        timer = display.EventLoop.AddTimer(Complete);
        timer.UpdateTimer(graceMillis <= 0 ? 1 : graceMillis);

        if (global.SupportsWithdrawnNotification)
        {
            global.Withdrawn += Complete;
        }

        global.Remove();
    }
}
