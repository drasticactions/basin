using System.Reflection;

namespace Basin.Ipc;

public sealed record IpcSessionInfo
{
    public required string Compositor { get; init; }

    public string? Backend { get; init; }

    public string? Renderer { get; init; }

    public string? WaylandSocket { get; init; }

    public Func<string?>? XwaylandDisplay { get; init; }

    public Action? Quit { get; init; }

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
