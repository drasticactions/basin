using System.Reflection;

namespace Basin.Ipc;

/// <summary>
/// What the program tells the control socket about its session. <c>session/describe</c> and <c>ipc/version</c> read it.
/// </summary>
public sealed record IpcSessionInfo
{
    /// <summary>
    /// Gets the compositor's name, as <c>ipc/version</c> and <c>session/describe</c> report it.
    /// </summary>
    public required string Compositor { get; init; }

    /// <summary>
    /// Gets the backend name, or <see langword="null"/> when the program does not report it.
    /// </summary>
    public string? Backend { get; init; }

    /// <summary>
    /// Gets the renderer name, or <see langword="null"/> when the program does not report it.
    /// </summary>
    public string? Renderer { get; init; }

    /// <summary>
    /// Gets the Wayland socket name or path. The default control socket path uses its file name.
    /// </summary>
    public string? WaylandSocket { get; init; }

    /// <summary>
    /// Gets a function that returns the current X11 display name, or <see langword="null"/> when the session has no Xwayland.
    /// </summary>
    public Func<string?>? XwaylandDisplay { get; init; }

    /// <summary>
    /// Gets the action that stops the compositor. Without it, <c>session/quit</c> is not registered.
    /// </summary>
    public Action? Quit { get; init; }

    /// <summary>
    /// Gets the source that <c>session/settle</c> waits on. Without it, <c>session/settle</c> is not registered.
    /// </summary>
    public Basin.Capabilities.IFrameSettle? Settle { get; init; }

    /// <summary>
    /// Gets the process tracker that the server uses for <c>process/*</c>. Without it, the server makes and disposes its own.
    /// </summary>
    public ProcessTracker? Processes { get; init; }

    /// <summary>
    /// Gets the basin version that <c>ipc/version</c> reports. The default is the informational version of this assembly without its build metadata.
    /// </summary>
    public string BasinVersion { get; init; } = DefaultVersion();

    private static string DefaultVersion()
    {
        var assembly = typeof(IpcSessionInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (informational is null)
        {
            return assembly.GetName().Version?.ToString() ?? "unknown";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }
}
