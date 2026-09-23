using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Vulkan;
using Prowl.Quill;
using Silk.NET.Vulkan;

namespace Basin.UI.Quill;

public sealed class QuillVulkanUIHost : IUIHost
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly VulkanDevice _device;
    private readonly IAllocator _allocator;
    private readonly FontAtlasSettings _atlas;
    private readonly QuillVulkanPipeline _pipeline;
    private readonly QuillVulkanTextures _textures;
    private readonly List<QuillVulkanUISurface> _surfaces = [];
    private readonly ulong[] _modifiers;
    private readonly bool _ownsDevice;
    private readonly bool _ownsAllocator;
    private bool _syncReported;
    private bool _disposed;

    public QuillVulkanUIHost(VulkanDevice device, IAllocator allocator, FontAtlasSettings? atlas = null)
        : this(device, allocator, RenderableModifiers(device, null) ?? [], ownsDevice: false, ownsAllocator: false, atlas)
    {
    }

    private QuillVulkanUIHost(
        VulkanDevice device, IAllocator allocator, ulong[] modifiers, bool ownsDevice, bool ownsAllocator,
        FontAtlasSettings? atlas)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(allocator);
        _device = device;
        _allocator = allocator;
        _modifiers = modifiers;
        _ownsDevice = ownsDevice;
        _ownsAllocator = ownsAllocator;
        _atlas = atlas ?? new FontAtlasSettings();
        _pipeline = new QuillVulkanPipeline(device);
        try
        {
            _textures = new QuillVulkanTextures(device);
        }
        catch
        {
            _pipeline.Dispose();
            throw;
        }
    }

    public static QuillVulkanUIHost? TryCreate(
        VulkanDevice? shared,
        string? renderNodePath,
        DrmFormatSet? imports,
        out string? declined,
        IAllocator? allocator = null,
        FontAtlasSettings? atlas = null)
    {
        if (shared is not null)
        {
            return TryBuild(shared, ownsDevice: false, imports, allocator, atlas, out declined);
        }

        if (string.IsNullOrEmpty(renderNodePath))
        {
            declined = "the renderer has no VulkanDevice to share and no render node was named for one of its own";
            return null;
        }

        if (imports is not null && !imports.Contains(DrmFormat.Argb8888))
        {
            declined = "the renderer imports no Argb8888 dmabuf";
            return null;
        }

        VulkanDevice device;
        try
        {
            device = new VulkanDevice(renderNodePath, ["VK_KHR_external_semaphore_fd", "VK_KHR_external_semaphore"]);
        }
        catch (Exception error) when (error is InvalidOperationException or DllNotFoundException
                                          or EntryPointNotFoundException)
        {
            declined = $"a VulkanDevice on {renderNodePath} would not build: {error.Message}";
            return null;
        }

        var host = TryBuild(device, ownsDevice: true, imports, allocator: null, atlas, out declined);
        if (host is null)
        {
            device.Dispose();
        }

        return host;
    }

    public bool OwnsDevice => _ownsDevice;

    public VulkanDevice Device => _device;

    internal int FencedFrames { get; private set; }

    internal int FinishedFrames { get; private set; }

    internal ReadOnlySpan<ulong> Modifiers => _modifiers;

    public Type SurfaceContract => typeof(IQuillUISurface);

    public UITargetKind Produces => UITargetKind.Dmabuf;

    public QuillVulkanTextures Textures
    {
        get
        {
            _thread.Assert();
            return _textures;
        }
    }

    public long? NextDueMillis => null;

    public event Action? WakeupRequested
    {
        add
        {
        }

        remove
        {
        }
    }

    public IUISurface? CreateSurface(in UISurfaceOptions options)
    {
        _thread.Assert();
        if (_disposed || options.Target != UITargetKind.Dmabuf)
        {
            return null;
        }

        var surface = Create();
        if (!surface.Configure(options.Width, options.Height, options.Scale))
        {
            surface.Dispose();
            return null;
        }

        return surface;
    }

    public void Pump()
    {
    }

    public void Dispose()
    {
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var surface in _surfaces.ToArray())
        {
            surface.Dispose();
        }

        _surfaces.Clear();
        _textures.Dispose();
        _pipeline.Dispose();
        if (_ownsAllocator)
        {
            _allocator.Dispose();
        }

        if (_ownsDevice)
        {
            _device.Dispose();
        }
    }

    internal QuillVulkanUISurface Create()
    {
        var surface = new QuillVulkanUISurface(_device, _allocator, _pipeline, _textures, _atlas, this);
        _surfaces.Add(surface);
        return surface;
    }

    internal void Forget(QuillVulkanUISurface surface) => _surfaces.Remove(surface);

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
            QuillVulkanLog.Log.Debug($"chrome on {_device.DevicePath} publishes a Vulkan sync file to each dmabuf it draws");
        }
        else
        {
            QuillVulkanLog.Log.Debug($"chrome on {_device.DevicePath} exports no sync file and waits on the queue for each frame");
        }
    }

    private static QuillVulkanUIHost? TryBuild(
        VulkanDevice device, bool ownsDevice, DrmFormatSet? imports, IAllocator? allocator, FontAtlasSettings? atlas,
        out string? declined)
    {
        var modifiers = RenderableModifiers(device, imports);
        if (modifiers is null)
        {
            declined = $"no Argb8888 modifier that {device.DevicePath} renders is one the renderer imports";
            return null;
        }

        var ownsAllocator = allocator is null;
        allocator ??= device.CreateAllocator();
        if (!Probe(device, allocator, modifiers))
        {
            if (ownsAllocator)
            {
                allocator.Dispose();
            }

            declined = $"{device.DevicePath} could not allocate and render into an Argb8888 chrome buffer";
            return null;
        }

        try
        {
            declined = null;
            return new QuillVulkanUIHost(device, allocator, modifiers, ownsDevice, ownsAllocator, atlas);
        }
        catch (InvalidOperationException error)
        {
            if (ownsAllocator)
            {
                allocator.Dispose();
            }

            declined = error.Message;
            return null;
        }
    }

    private static ulong[]? RenderableModifiers(VulkanDevice device, DrmFormatSet? imports)
    {
        var renderable = imports is null ? device.RenderableFormats : device.RenderableFormats.Intersect(imports);
        var chosen = new List<ulong>();
        foreach (var modifier in renderable.ModifiersOf(DrmFormat.Argb8888))
        {
            if (modifier != DrmFormatSet.ModifierInvalid)
            {
                chosen.Add(modifier);
            }
        }

        return chosen.Count > 0 ? [.. chosen] : null;
    }

    private static bool Probe(VulkanDevice device, IAllocator allocator, ulong[] modifiers)
    {
        var buffer = allocator.Allocate(16, 16, DrmFormat.Argb8888, modifiers, BufferUse.Render | BufferUse.Scanout);
        if (buffer is null)
        {
            return false;
        }

        try
        {
            if (!buffer.TryGetDmabuf(out var attributes))
            {
                return false;
            }

            using var image = VulkanDeviceImage.TryImport(device, attributes, ImageUsageFlags.ColorAttachmentBit);
            return image is not null;
        }
        finally
        {
            if (buffer is BufferBase concrete)
            {
                concrete.Destroy();
            }
        }
    }
}
