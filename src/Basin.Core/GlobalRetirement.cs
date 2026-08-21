using Wayland.Server;

namespace Basin;

public static class GlobalRetirement
{
    public const int DefaultGraceMillis = 5000;

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
