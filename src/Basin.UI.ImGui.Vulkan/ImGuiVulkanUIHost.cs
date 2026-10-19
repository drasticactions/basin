using Basin.Capabilities;
using Basin.Render.Vulkan;
using Silk.NET.Vulkan;

namespace Basin.UI.ImGui;

public static class ImGuiVulkanUIHost
{
    public static ImGuiUIHost? TryCreate(
        VulkanDevice? shared,
        string? renderNodePath,
        DrmFormatSet? imports,
        out string? declined,
        IAllocator? allocator = null,
        ImGuiHostOptions? options = null)
    {
        if (!ImGuiNative.TryLoad(out declined))
        {
            return null;
        }

        if (shared is not null)
        {
            return TryBuild(shared, ownsDevice: false, imports, allocator, options, out declined);
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

        var host = TryBuild(device, ownsDevice: true, imports, allocator: null, options, out declined);
        if (host is null)
        {
            device.Dispose();
        }

        return host;
    }

    private static ImGuiUIHost? TryBuild(
        VulkanDevice device, bool ownsDevice, DrmFormatSet? imports, IAllocator? allocator, ImGuiHostOptions? options,
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

        ImGuiVulkanBackend? backend = null;
        try
        {
            backend = new ImGuiVulkanBackend(device, allocator, modifiers, ownsAllocator);
            var host = new ImGuiUIHost(backend, options);
            backend.TakeDevice(ownsDevice);
            declined = null;
            return host;
        }
        catch (InvalidOperationException error)
        {
            if (backend is null)
            {
                if (ownsAllocator)
                {
                    allocator.Dispose();
                }
            }
            else
            {
                backend.Dispose();
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
