using Basin.Diagnostics;

namespace Basin.Host;

/// <summary>
/// Decides if a compositor serves accessibility, from the configured mode and the way the host runs.
/// </summary>
public static class AccessibilityGate
{
    /// <summary>
    /// Decides if accessibility is on. In <see cref="AccessibilityMode.Auto"/> it is on for the DRM backend and for a greeter, and off when nested or headless.
    /// </summary>
    /// <param name="mode">The configured mode.</param>
    /// <param name="host">The running host.</param>
    /// <param name="reason">The reason for the decision, for the log.</param>
    /// <returns><see langword="true"/> when the compositor serves accessibility.</returns>
    public static bool IsOn(AccessibilityMode mode, BasinHost host, out string reason) => IsOn(mode, host.Options, out reason);

    /// <summary>
    /// The environment variable that overrides the configured mode with <c>auto</c>, <c>on</c> or <c>off</c>, for every program.
    /// </summary>
    public const string OverrideVariable = "BASIN_ACCESSIBILITY";

    /// <summary>
    /// Decides if accessibility is on for host options. In <see cref="AccessibilityMode.Auto"/> it is on for the DRM backend and for a greeter, and off when nested or headless.
    /// A value of <see cref="OverrideVariable"/> replaces the configured mode.
    /// </summary>
    /// <param name="mode">The configured mode.</param>
    /// <param name="options">The options that the host started with.</param>
    /// <param name="reason">The reason for the decision, for the log.</param>
    /// <returns><see langword="true"/> when the compositor serves accessibility.</returns>
    public static bool IsOn(AccessibilityMode mode, HostOptions options, out string reason)
    {
        var forced = Environment.GetEnvironmentVariable(OverrideVariable) is { Length: > 0 } value ? value : null;
        if (forced is "auto" or "on" or "off")
        {
            mode = forced switch
            {
                "on" => AccessibilityMode.On,
                "off" => AccessibilityMode.Off,
                _ => AccessibilityMode.Auto,
            };
        }

        switch (mode)
        {
            case AccessibilityMode.On:
                reason = forced is null ? "enabled = \"on\"" : $"{OverrideVariable}=on";
                return true;
            case AccessibilityMode.Off:
                reason = forced is null ? "enabled = \"off\"" : $"{OverrideVariable}=off";
                return false;
        }

        if (options.Greeter)
        {
            reason = "a greeter starts the session";
            return true;
        }

        switch (options.Backend)
        {
            case HostBackend.Drm:
                reason = "the DRM backend is a full session";
                return true;
            case HostBackend.Nested:
                reason = "a nested compositor leaves accessibility to the parent session";
                return false;
            default:
                reason = "a headless compositor has no user";
                return false;
        }
    }

    /// <summary>
    /// Decides if accessibility is on, then runs the start step when it is. It logs the reason and prints <c>A11Y on</c> or <c>A11Y off</c>.
    /// </summary>
    /// <param name="mode">The configured mode.</param>
    /// <param name="host">The running host.</param>
    /// <param name="start">Starts the accessibility tree. It returns <see langword="null"/> on success, or the reason that it cannot start.</param>
    /// <param name="log">The logger for the reason.</param>
    /// <param name="whenOn">Runs once before the tree starts when the decision is on, for example to start the screen reader interfaces. It can be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when accessibility runs.</returns>
    public static async Task<bool> StartAsync(AccessibilityMode mode, BasinHost host, Func<Task<string?>> start, BasinLogger log, Action? whenOn = null)
    {
        if (!IsOn(mode, host, out var why))
        {
            log.Info($"accessibility: off, {why}");
            BasinReport.Line("A11Y off");
            return false;
        }

        whenOn?.Invoke();

        if (await start() is { } failure)
        {
            log.Info($"accessibility: no tree, {failure}");
            BasinReport.Line("A11Y off");
            return false;
        }

        log.Info($"accessibility: on, {why}");
        BasinReport.Line("A11Y on");
        return true;
    }
}
