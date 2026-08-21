namespace Basin.Capabilities;

public interface IUIHost : IDisposable
{
    UITargetKind Produces { get; }

    IUISurface? CreateSurface(in UISurfaceOptions options);

    long? NextDueMillis { get; }

    event Action? WakeupRequested;

    event Action<IUISurface>? PopupAppeared
    {
        add
        {
        }

        remove
        {
        }
    }

    event Action<IUISurface>? PopupDismissed
    {
        add
        {
        }

        remove
        {
        }
    }

    void Pump();
}
