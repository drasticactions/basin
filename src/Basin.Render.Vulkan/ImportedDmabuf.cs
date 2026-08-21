using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

internal sealed unsafe class ImportedDmabuf
{
    public Image Image;
    public ImageView View;
    public DeviceMemory[] Memories = [];
    public int Width;
    public int Height;
    public bool HasAlpha;

    public bool UsingMutableSrgb;

    public void Destroy(VulkanDevice device)
    {
        var vk = device.Api;
        if (View.Handle != 0)
        {
            vk.DestroyImageView(device.Device, View, null);
            View = default;
        }

        if (Image.Handle != 0)
        {
            vk.DestroyImage(device.Device, Image, null);
            Image = default;
        }

        foreach (var memory in Memories)
        {
            if (memory.Handle != 0)
            {
                vk.FreeMemory(device.Device, memory, null);
            }
        }

        Memories = [];
    }
}
