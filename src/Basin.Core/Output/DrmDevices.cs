namespace Basin;

/// <summary>
/// Finds DRM devices through <c>/dev/dri</c> and sysfs, without opening a device.
/// </summary>
public static class DrmDevices
{
    /// <summary>
    /// Lists every card node in a device directory, sorted by path. Sysfs paths are always read from <c>/sys/class/drm</c>.
    /// </summary>
    /// <param name="devDir">The directory that holds the card nodes.</param>
    /// <returns>The cards found, or an empty list when the directory does not exist.</returns>
    public static IReadOnlyList<DrmDeviceInfo> Enumerate(string devDir = "/dev/dri")
    {
        var devices = new List<DrmDeviceInfo>();
        if (!Directory.Exists(devDir))
        {
            return devices;
        }

        foreach (var card in Directory.GetFiles(devDir)
                     .Where(p => IsCardNode(Path.GetFileName(p)))
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(card);
            devices.Add(new DrmDeviceInfo
            {
                CardPath = card,
                RenderNodePath = FindRenderNode(name, devDir),
                Driver = DriverOf(name),
                IsBootVga = ReadFlag($"/sys/class/drm/{name}/device/boot_vga"),
                HasConnectors = HasAnyConnector(name),
            });
        }

        return devices;
    }

    /// <summary>
    /// Picks the card to drive displays from. The boot VGA device wins, then the first card with connectors.
    /// </summary>
    /// <param name="devices">The cards to choose from, in order of preference.</param>
    /// <returns>The chosen card, or <see langword="null"/> when no card has connectors.</returns>
    public static DrmDeviceInfo? PickPrimary(IReadOnlyList<DrmDeviceInfo> devices)
    {
        DrmDeviceInfo? fallback = null;
        foreach (var device in devices)
        {
            if (!device.HasConnectors)
            {
                continue;
            }

            if (device.IsBootVga)
            {
                return device;
            }

            fallback ??= device;
        }

        return fallback;
    }

    /// <summary>
    /// Reads the <c>dev_t</c> number of a DRM node from sysfs, in the Linux <c>makedev</c> encoding that dmabuf feedback uses.
    /// </summary>
    /// <param name="devicePath">The path of a card or render node. Only its file name is used.</param>
    /// <param name="deviceId">The device number, or zero when the call fails.</param>
    /// <returns><see langword="true"/> when sysfs gave a valid major and minor number.</returns>
    public static bool TryDeviceId(string devicePath, out ulong deviceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(devicePath);
        deviceId = 0;
        try
        {
            var name = Path.GetFileName(devicePath);
            var text = File.ReadAllText($"/sys/class/drm/{name}/dev").Trim().Split(':');
            if (text.Length != 2 || !ulong.TryParse(text[0], out var major) || !ulong.TryParse(text[1], out var minor))
            {
                return false;
            }

            deviceId = ((major & 0xFFF) << 8) | (minor & 0xFF) | ((minor & ~0xFFul) << 12) | ((major & ~0xFFFul) << 32);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsCardNode(string name) =>
        name.StartsWith("card", StringComparison.Ordinal) && name.Skip(4).All(char.IsAsciiDigit);

    private static string? FindRenderNode(string cardName, string devDir)
    {
        var siblings = $"/sys/class/drm/{cardName}/device/drm";
        if (!Directory.Exists(siblings))
        {
            return null;
        }

        foreach (var entry in Directory.GetDirectories(siblings).OrderBy(p => p, StringComparer.Ordinal))
        {
            var node = Path.GetFileName(entry);
            if (node.StartsWith("renderD", StringComparison.Ordinal))
            {
                return Path.Combine(devDir, node);
            }
        }

        return null;
    }

    private static string? DriverOf(string cardName)
    {
        try
        {
            return Directory.ResolveLinkTarget($"/sys/class/drm/{cardName}/device/driver", returnFinalTarget: true)?.Name;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static bool HasAnyConnector(string cardName)
    {
        var prefix = cardName + "-";
        return Directory.Exists("/sys/class/drm") &&
            Directory.EnumerateDirectories("/sys/class/drm").Any(p => Path.GetFileName(p).StartsWith(prefix, StringComparison.Ordinal));
    }

    private static bool ReadFlag(string path)
    {
        try
        {
            return File.Exists(path) && File.ReadAllText(path).Trim() == "1";
        }
        catch (IOException)
        {
            return false;
        }
    }
}
