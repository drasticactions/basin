using Basin;
using Basin.Shell.Weston;

namespace Westonia;

internal sealed class ShellRolesImpl : IShellRoles
{
    private readonly WestonShell _shell;

    public ShellRolesImpl(WestonShell shell) => _shell = shell;

    public void SetBackground(IOutput output, Surface surface) => _shell.AdoptBackground(output, surface);

    public void SetPanel(IOutput output, Surface surface) => _shell.AdoptPanel(output, surface);

    public void SetPanelPosition(ShellPanelPosition position) => _shell.SetPanelPosition(position);

    public void SetLockSurface(Surface surface) => _shell.AdoptLockSurface(surface);

    public void Unlock() => _shell.Unlock();

    public void SetGrabSurface(Surface surface) => _shell.AdoptGrabSurface(surface);

    public void DesktopReady() => _shell.DesktopReady();

    public void SetScreensaverSurface(IOutput output, Surface surface) =>
        _shell.AdoptScreensaverSurface(output, surface);
}
