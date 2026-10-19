using Basin.Capabilities;
using Basin.Render.Vulkan;

namespace Basin.UI.ImGui;

internal sealed unsafe class ImGuiVulkanBackend : IImGuiBackend
{
    private readonly List<ImGuiVulkanTarget> _targets = [];
    private readonly ulong[] _modifiers;
    private bool _ownsDevice;
    private readonly bool _ownsAllocator;
    private bool _syncReported;
    private bool _disposed;

    internal ImGuiVulkanBackend(VulkanDevice device, IAllocator allocator, ulong[] modifiers, bool ownsAllocator)
    {
        Device = device;
        Allocator = allocator;
        _modifiers = modifiers;
        _ownsAllocator = ownsAllocator;
        Pipeline = new ImGuiVulkanPipeline(device);
        Textures = new ImGuiVulkanTextures(device, Pipeline);
    }

    public string Name => "vulkan";

    public UITargetKind Produces => UITargetKind.Dmabuf;

    public bool HasVertexOffset => true;

    public int FramesInFlight => 3;

    internal VulkanDevice Device { get; }

    internal IAllocator Allocator { get; }

    internal ImGuiVulkanPipeline Pipeline { get; }

    internal ImGuiVulkanTextures Textures { get; }

    internal bool OwnsDevice => _ownsDevice;

    internal ReadOnlySpan<ulong> Modifiers => _modifiers;

    internal int FencedFrames { get; private set; }

    internal int FinishedFrames { get; private set; }

    public IImGuiTarget? CreateTarget()
    {
        if (_disposed)
        {
            return null;
        }

        var target = new ImGuiVulkanTarget(this);
        _targets.Add(target);
        return target;
    }

    public int CreateTexture(int width, int height) => _disposed ? -1 : Textures.Create(width, height);

    public void UpdateTexture(int texture, int x, int y, int width, int height, byte* pixels, int pitch) =>
        Textures.Update(texture, x, y, width, height, pixels, pitch);

    public void DestroyTexture(int texture) => Textures.Destroy(texture);

    internal void TakeDevice(bool owns) => _ownsDevice = owns;

    internal void Forget(ImGuiVulkanTarget target) => _targets.Remove(target);

    internal void ReportSync(bool fenced)
    {
        if (fenced)
        {
            FencedFrames++;
        }
        else
        {
            FinishedFrames++;
        }

        if (_syncReported)
        {
            return;
        }

        _syncReported = true;
        if (fenced)
        {
            ImGuiVulkanLog.Log.Debug($"chrome on {Device.DevicePath} publishes a Vulkan sync file to each dmabuf it draws");
        }
        else
        {
            ImGuiVulkanLog.Log.Debug($"chrome on {Device.DevicePath} exports no sync file and waits on the queue for each frame");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var target in _targets.ToArray())
        {
            target.Dispose();
        }

        _targets.Clear();
        Textures.Dispose();
        Pipeline.Dispose();
        if (_ownsAllocator)
        {
            Allocator.Dispose();
        }

        if (_ownsDevice)
        {
            Device.Dispose();
        }
    }
}
