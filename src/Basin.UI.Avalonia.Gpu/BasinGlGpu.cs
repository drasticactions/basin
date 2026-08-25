using Avalonia.OpenGL.Egl;
using Avalonia.Platform;
using Basin.Diagnostics;
using Basin.Render.Gl;

namespace Basin.UI.Avalonia;

public sealed class BasinGlGpu : IAvaloniaGpu, IDisposable
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly GlDevice _device;
    private readonly IAllocator _allocator;
    private readonly BasinGlGraphics _graphics;
    private readonly ulong[] _modifiers;
    private readonly bool _ownsDevice;
    private bool _disposed;

    public BasinGlGpu(GlDevice device, IAllocator allocator, DrmFormatSet? imports = null, bool ownsDevice = false)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(allocator);

        _device = device;
        _allocator = allocator;
        _modifiers = ChooseModifiers(device, imports);
        _ownsDevice = ownsDevice;
        var display = new EglDisplay(device.Egl.Handle, new EglDisplayOptions
        {
            SupportsContextSharing = true,
            SupportsMultipleContexts = true,
        });
        _graphics = new BasinGlGraphics(display);
    }

    public static BasinGlGpu? TryCreate(string? renderNodePath, GlDevice? shared = null, DrmFormatSet? imports = null)
    {
        try
        {
            if (shared is not null)
            {
                return new BasinGlGpu(shared, shared.CreateAllocator(), imports);
            }

            if (string.IsNullOrEmpty(renderNodePath))
            {
                return null;
            }

            var device = new GlDevice(renderNodePath);
            try
            {
                return new BasinGlGpu(device, device.CreateAllocator(), imports, ownsDevice: true);
            }
            catch
            {
                device.Dispose();
                throw;
            }
        }
        catch
        {
            return null;
        }
    }

    public IPlatformGraphics Graphics => _graphics;

    public IAvaloniaGpuTarget CreateTarget()
    {
        _thread.Assert();
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new BasinGlGpuTarget(_device, _allocator, _modifiers);
    }

    private static ulong[] ChooseModifiers(GlDevice device, DrmFormatSet? imports)
    {
        var renderable = device.SampleableFormats;
        var usable = imports is null || imports.Count == 0 ? renderable : renderable.Intersect(imports);
        var chosen = new List<ulong>();
        foreach (var modifier in usable.ModifiersOf(DrmFormat.Argb8888))
        {
            if (modifier != DrmFormatSet.ModifierInvalid)
            {
                chosen.Add(modifier);
            }
        }

        return [.. chosen];
    }

    public void Dispose()
    {
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _graphics.DisposeContexts();
        if (_ownsDevice)
        {
            _device.Dispose();
        }
    }
}
