using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Basin.Tests;

internal static unsafe class VulkanRunnability
{
    private const string ModifierExtension = "VK_EXT_image_drm_format_modifier";

    private static readonly Dictionary<string, string?> _blockers = [];

    public static bool Runnable => BlockerFor(CompositorTestHost.RenderNodePath) is null;

    public static string Blocker => BlockerFor(CompositorTestHost.RenderNodePath) ?? "";

    public static string? BlockerFor(string node)
    {
        if (!_blockers.TryGetValue(node, out var blocker))
        {
            blocker = Probe(node);
            _blockers[node] = blocker;
        }

        return blocker;
    }

    private static string? Probe(string node)
    {
        if (!File.Exists(node))
        {
            return "no render node";
        }

        Vk vk;
        try
        {
            vk = Vk.GetApi();
        }
        catch (Exception e) when (e is DllNotFoundException or FileNotFoundException or EntryPointNotFoundException)
        {
            return "no Vulkan loader";
        }

        var appInfo = new ApplicationInfo
        {
            SType = StructureType.ApplicationInfo,
            ApiVersion = Vk.Version12,
        };
        var instanceInfo = new InstanceCreateInfo
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &appInfo,
        };
        Instance instance;
        try
        {
            if (vk.CreateInstance(in instanceInfo, null, out instance) != Result.Success)
            {
                return "no Vulkan instance";
            }
        }
        catch (Exception e) when (e is DllNotFoundException or FileNotFoundException or EntryPointNotFoundException)
        {
            return "no Vulkan loader";
        }

        try
        {
            uint count = 0;
            if (vk.EnumeratePhysicalDevices(instance, ref count, null) != Result.Success || count == 0)
            {
                return "no Vulkan physical devices";
            }

            var devices = new PhysicalDevice[count];
            fixed (PhysicalDevice* devicesPtr = devices)
            {
                if (vk.EnumeratePhysicalDevices(instance, ref count, devicesPtr) != Result.Success)
                {
                    return "no Vulkan physical devices";
                }
            }

            var (major, minor) = DevNumbersOf(node);
            PhysicalDevice candidate = default;
            var found = false;
            foreach (var device in devices)
            {
                var drm = new PhysicalDeviceDrmPropertiesEXT { SType = StructureType.PhysicalDeviceDrmPropertiesExt };
                var properties = new PhysicalDeviceProperties2
                {
                    SType = StructureType.PhysicalDeviceProperties2,
                    PNext = &drm,
                };
                vk.GetPhysicalDeviceProperties2(device, &properties);
                if ((drm.HasRender.Value != 0 && drm.RenderMajor == major && drm.RenderMinor == minor) ||
                    (drm.HasPrimary.Value != 0 && drm.PrimaryMajor == major && drm.PrimaryMinor == minor))
                {
                    candidate = device;
                    found = true;
                    break;
                }
            }

            if (!found && count == 1)
            {
                candidate = devices[0];
                found = true;
            }

            if (!found)
            {
                return $"no Vulkan device serves {node}";
            }

            vk.GetPhysicalDeviceProperties(candidate, out var candidateProperties);
            var name = Marshal.PtrToStringAnsi((nint)candidateProperties.DeviceName) ?? "unnamed device";
            if (candidateProperties.ApiVersion < Vk.Version12)
            {
                return $"{name} predates Vulkan 1.2";
            }

            return HasExtension(vk, candidate, ModifierExtension)
                ? null
                : $"{name} has no {ModifierExtension}";
        }
        finally
        {
            vk.DestroyInstance(instance, null);
        }
    }

    private static bool HasExtension(Vk vk, PhysicalDevice device, string extension)
    {
        uint count = 0;
        if (vk.EnumerateDeviceExtensionProperties(device, (byte*)null, ref count, null) != Result.Success)
        {
            return false;
        }

        var properties = new ExtensionProperties[count];
        fixed (ExtensionProperties* propertiesPtr = properties)
        {
            if (vk.EnumerateDeviceExtensionProperties(device, (byte*)null, ref count, propertiesPtr) != Result.Success)
            {
                return false;
            }
        }

        foreach (var property in properties)
        {
            if (Marshal.PtrToStringAnsi((nint)property.ExtensionName) == extension)
            {
                return true;
            }
        }

        return false;
    }

    private static (long Major, long Minor) DevNumbersOf(string node)
    {
        try
        {
            var numbers = File.ReadAllText($"/sys/class/drm/{Path.GetFileName(node)}/dev").Trim().Split(':');
            return (long.Parse(numbers[0]), long.Parse(numbers[1]));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or IndexOutOfRangeException)
        {
            return (-1, -1);
        }
    }
}
