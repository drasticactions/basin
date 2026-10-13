using Basin.Session;
using Drm;

namespace Basin.Tests;

internal static class VkmsCard
{
    public static string? Find()
    {
        if (!Directory.Exists("/dev/dri"))
        {
            return null;
        }

        foreach (var node in Directory.GetFiles("/dev/dri", "card*").OrderBy(p => p, StringComparer.Ordinal))
        {
            try
            {
                using var session = new DirectSession();
                using var device = session.OpenDevice(node);
                using var drm = DrmDevice.FromFd(device.FileDescriptor, ownsFd: false);
                if (drm.GetVersion().Name == "vkms")
                {
                    return node;
                }
            }
            catch (Exception e) when (e is DrmException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
            }
        }

        return null;
    }
}
