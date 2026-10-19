using Basin.Capabilities;
using Basin.Render.Gl;

namespace Basin.UI.ImGui;

public static class ImGuiGlUIHost
{
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
            if (buffer is BufferBase concrete)
            {
                concrete.Destroy();
            }
        }
    }
}
