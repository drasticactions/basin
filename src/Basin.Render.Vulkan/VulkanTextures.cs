using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

/// <summary>
/// Opens the textures of a <see cref="VulkanRenderer"/> for code that records its own Vulkan commands.
/// </summary>
public static class VulkanTextures
{
    /// <summary>
    /// Gets the Vulkan handles of a texture and records any pending upload of a shared-memory texture.
    /// </summary>
    /// <param name="device">The device that the caller records on. A texture from another device fails.</param>
    /// <param name="texture">The texture to open.</param>
    /// <param name="uploads">The command buffer that receives the upload copy. It must execute before the caller samples the view.</param>
    /// <param name="view">The handles of the texture, or the default value on failure.</param>
    /// <returns><see langword="false"/> when the texture is foreign, a YCbCr dmabuf, or a shared-memory texture whose upload failed.</returns>
    public static bool TryOpen(VulkanDevice device, ITexture texture, CommandBuffer uploads, out VulkanTextureView view)
    {
        ArgumentNullException.ThrowIfNull(device);
        switch (texture)
        {
            case VulkanDmabufTexture dmabuf when ReferenceEquals(dmabuf.Device, device) && dmabuf.Ycbcr is null:
                view = new VulkanTextureView(
                    dmabuf.Image, dmabuf.View, dmabuf.VkFormat, ImageLayout.General,
                    dmabuf.Width, dmabuf.Height, true, dmabuf.HasAlpha, true);
                return true;
            case VulkanShmTexture shm when ReferenceEquals(shm.Device, device):
                if (!shm.PrepareUpload(device.Staging))
                {
                    break;
                }

                if (shm.NeedsGpuCopy)
                {
                    shm.RecordGpuCopy(uploads);
                }

                view = new VulkanTextureView(
                    shm.Image, shm.View, shm.VkFormat, ImageLayout.General,
                    shm.Width, shm.Height, true, shm.HasAlpha, false);
                return true;
        }

        view = default;
        return false;
    }

    /// <summary>
    /// Gets the dmabuf attributes that a dmabuf texture was imported from.
    /// </summary>
    /// <param name="texture">The texture to examine.</param>
    /// <param name="attributes">The attributes. The imported buffer owns their fds.</param>
    /// <returns><see langword="true"/> when the texture is a Vulkan dmabuf texture.</returns>
    public static bool TryGetDmabuf(ITexture texture, out DmabufAttributes attributes)
    {
        if (texture is VulkanDmabufTexture dmabuf)
        {
            attributes = dmabuf.Attributes;
            return true;
        }

        attributes = default;
        return false;
    }
}
