namespace Basin.Shell.Weston;

/// <summary>
/// The shell policy that weston's shell requests drive. Its members map one for one to <c>weston_desktop_shell</c> and <c>weston_screensaver</c> requests, so an in-process shell and a shell client make the same calls.
/// </summary>
public interface IShellRoles
{
    /// <summary>
    /// Makes a surface the background of an output.
    /// </summary>
    /// <param name="output">The output that shows the background.</param>
    /// <param name="surface">The surface that becomes the background.</param>
    void SetBackground(IOutput output, Surface surface);

    /// <summary>
    /// Makes a surface the panel of an output.
    /// </summary>
    /// <param name="output">The output that shows the panel.</param>
    /// <param name="surface">The surface that becomes the panel.</param>
    void SetPanel(IOutput output, Surface surface);

    /// <summary>
    /// Sets the screen edge that the panels use, so that new and maximized windows do not overlap them.
    /// </summary>
    /// <param name="position">The screen edge of the panels.</param>
    void SetPanelPosition(ShellPanelPosition position);

    /// <summary>
    /// Makes a surface the dialog that asks the user to unlock the screen.
    /// </summary>
    /// <param name="surface">The surface that becomes the lock dialog.</param>
    void SetLockSurface(Surface surface);

    /// <summary>
    /// Ends the lock and resumes the normal desktop. The shell client sends it after a lock prompt, also when it does not implement locking.
    /// </summary>
    void Unlock();

    /// <summary>
    /// Sets the surface that gets a fake pointer enter at position 0, 0 during a compositor grab. The shell client sets the grab cursor on it.
    /// </summary>
    /// <param name="surface">The surface that receives the grab pointer focus.</param>
    void SetGrabSurface(Surface surface);

    /// <summary>
    /// Tells the compositor that the shell drew enough of the desktop to show it. A compositor can keep the screen black until this call.
    /// </summary>
    void DesktopReady();

    /// <summary>
    /// Makes a surface the screensaver of an output. A screensaver surface stays hidden until an idle timeout.
    /// </summary>
    /// <param name="output">The output that shows the screensaver.</param>
    /// <param name="surface">The surface that becomes the screensaver.</param>
    void SetScreensaverSurface(IOutput output, Surface surface);
}
