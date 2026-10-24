namespace Basin.Capabilities;

/// <summary>
/// Moves an X11 application to the X server of another density, by writing a launch rule and relaunching it.
/// </summary>
public interface IX11DensityControl
{
    /// <summary>
    /// Starts a density change for the application that owns a window.
    /// </summary>
    /// <param name="toplevelId">The toplevel id of an X11 window.</param>
    /// <param name="density">The density to move the application to.</param>
    /// <param name="completed">Called once with the result when the call returns <see langword="true"/>. It can run before this call returns, or later on the compositor loop.</param>
    /// <param name="error">A readable reason when the call returns <see langword="false"/>, otherwise <see langword="null"/>.</param>
    /// <returns><see langword="false"/> when the window is not X11, or no rule can be written for it.</returns>
    bool TrySet(ulong toplevelId, X11Density density, Action<X11DensityResult> completed, out string? error);
}
