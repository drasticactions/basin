namespace Basin;

public static class DrmDevices
{
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
