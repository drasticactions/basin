namespace Basin;

/// <summary>
/// Describes one DRM card node as <see cref="DrmDevices.Enumerate"/> found it in sysfs.
/// </summary>
public sealed class DrmDeviceInfo
{
    /// <summary>
    /// Gets the path of the primary card node, for example <c>/dev/dri/card0</c>.
    /// </summary>
    public required string CardPath { get; init; }

    /// <summary>
    /// Gets the path of the render node of the same device, or <see langword="null"/> when sysfs lists none.
    /// </summary>
    public string? RenderNodePath { get; init; }

    /// <summary>
    /// Gets the name of the kernel driver that the PCI or platform device is bound to, or <see langword="null"/> when sysfs does not tell.
    /// </summary>
    public string? Driver { get; init; }

    /// <summary>
    /// Gets a value indicating whether the firmware used this device as the boot display.
    /// </summary>
    public bool IsBootVga { get; init; }

    /// <summary>
    /// Gets a value indicating whether sysfs lists at least one connector for this card. A render-only device has none.
    /// </summary>
    public bool HasConnectors { get; init; }
}
