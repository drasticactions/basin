namespace Basin.Shell.Weston;

public interface IShellRoles
{
    void SetBackground(IOutput output, Surface surface);

    void SetPanel(IOutput output, Surface surface);

    void SetPanelPosition(ShellPanelPosition position);

    void SetLockSurface(Surface surface);

    void Unlock();

    void SetGrabSurface(Surface surface);

    void DesktopReady();

    void SetScreensaverSurface(IOutput output, Surface surface);
}
