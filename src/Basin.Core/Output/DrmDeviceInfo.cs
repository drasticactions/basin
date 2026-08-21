namespace Basin;

public sealed class DrmDeviceInfo
{
    public required string CardPath { get; init; }

    public string? RenderNodePath { get; init; }

    public string? Driver { get; init; }

    public bool IsBootVga { get; init; }

    public bool HasConnectors { get; init; }
}
