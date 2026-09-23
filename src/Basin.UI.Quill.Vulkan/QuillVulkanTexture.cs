using Silk.NET.Vulkan;

namespace Basin.UI.Quill;

public sealed class QuillVulkanTexture : QuillTexture
{
    internal QuillVulkanTexture(Image image, DeviceMemory memory, ImageView view, int width, int height)
        : base(width, height)
    {
        Image = image;
        Memory = memory;
        View = view;
    }

    public Image Image { get; }

    public ImageView View { get; }

    internal DeviceMemory Memory { get; }

    internal bool Initialized { get; set; }

    internal int Stamp { get; set; }

    internal ulong RetiredAt { get; set; }
}
