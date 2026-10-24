using Basin.Capabilities;
using Basin.Render.Gl;

namespace Basin.UI.ImGui;

/// <summary>
/// Creates an <see cref="ImGuiUIHost"/> that renders with OpenGL ES into Argb8888 dmabufs.
/// </summary>
public static class ImGuiGlUIHost
{
    /// <summary>
    /// Creates a GL host on the GL device of the renderer, or on a device of its own. The host checks that it can allocate and render a chrome buffer before it returns.
    /// </summary>
    /// <param name="shared">The GL device of the renderer, which the host uses and does not own. When it is <see langword="null"/>, the host opens its own device on <paramref name="renderNodePath"/>.</param>
    /// <param name="renderNodePath">The DRM render node for a device of its own, for example /dev/dri/renderD128.</param>
    /// <param name="imports">The dmabuf formats that the renderer imports, or <see langword="null"/> to let the allocator choose the modifier.</param>
    /// <param name="declined">The reason that no host was made, or <see langword="null"/> on success.</param>
    /// <param name="allocator">The buffer allocator for a shared device, which the host does not own. When it is <see langword="null"/>, or the host opens its own device, the host makes and owns an allocator.</param>
    /// <param name="options">The host options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The host, or <see langword="null"/> when ImGui does not load, no device is available, or no buffer that the renderer imports can be rendered.</returns>
    public static ImGuiUIHost? TryCreate(
        GlDevice? shared,
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
            declined = "the renderer has no GlDevice to share and no render node was named for one of its own";
            return null;
        }

        if (imports is not null && !imports.Contains(DrmFormat.Argb8888))
        {
            declined = "the renderer imports no Argb8888 dmabuf";
            return null;
        }

        var saved = ImGuiGlContext.Save();
        GlDevice device;
        try
        {
            device = new GlDevice(renderNodePath);
        }
        catch (Exception error) when (error is InvalidOperationException or DllNotFoundException
                                          or EntryPointNotFoundException or Mesa.Egl.EglException
                                          or Mesa.Gbm.GbmException)
        {
            saved.Dispose();
            declined = $"a GlDevice on {renderNodePath} would not build: {error.Message}";
            return null;
        }

        saved.Dispose();
        var host = TryBuild(device, ownsDevice: true, imports, allocator: null, options, out declined);
        if (host is null)
        {
            saved = ImGuiGlContext.Save();
            device.Dispose();
            saved.Dispose();
        }

        return host;
    }

    private static ImGuiUIHost? TryBuild(
        GlDevice device, bool ownsDevice, DrmFormatSet? imports, IAllocator? allocator, ImGuiHostOptions? options,
        out string? declined)
    {
        var modifiers = ChooseModifiers(device, imports);
        if (modifiers is null)
        {
            declined = $"no Argb8888 modifier that {device.DevicePath} renders is one the renderer imports";
            return null;
        }

        var ownsAllocator = allocator is null;
        allocator ??= device.CreateAllocator();
        if (!Probe(device, allocator, modifiers, ownsDevice))
        {
            if (ownsAllocator)
            {
                allocator.Dispose();
            }

            declined = $"{device.DevicePath} could not allocate and render into an Argb8888 chrome buffer";
            return null;
        }

        ImGuiGlBackend? backend = null;
        try
        {
            backend = new ImGuiGlBackend(device, allocator, modifiers, separateContext: ownsDevice, ownsAllocator);
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

    private static ulong[]? ChooseModifiers(GlDevice device, DrmFormatSet? imports)
    {
        if (imports is null)
        {
            return [];
        }

        var chosen = new List<ulong>();
        foreach (var modifier in device.SampleableFormats.Intersect(imports).ModifiersOf(DrmFormat.Argb8888))
        {
            if (modifier != DrmFormatSet.ModifierInvalid)
            {
                chosen.Add(modifier);
            }
        }

        if (chosen.Count > 0)
        {
            return [.. chosen];
        }

        return imports.Contains(DrmFormat.Argb8888, DrmFormatSet.ModifierInvalid)
               && device.SampleableFormats.Contains(DrmFormat.Argb8888, DrmFormatSet.ModifierInvalid)
            ? []
            : null;
    }

    private static bool Probe(GlDevice device, IAllocator allocator, ulong[] modifiers, bool ownsDevice)
    {
        var buffer = allocator.Allocate(16, 16, DrmFormat.Argb8888, modifiers, BufferUse.Render | BufferUse.Scanout);
        if (buffer is null)
        {
            return false;
        }

        using var current = new ImGuiGlContext(device, ownsDevice).Enter();
        try
        {
            GlRenderTarget.Create(device, buffer).Dispose(device);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        finally
        {
            buffer?.Destroy();
        }
    }
}
