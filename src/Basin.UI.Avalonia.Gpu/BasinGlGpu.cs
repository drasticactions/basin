using Avalonia.OpenGL.Egl;
using Avalonia.Platform;
using Basin.Diagnostics;
using Basin.Render.Gl;

namespace Basin.UI.Avalonia;

/// <summary>
/// An <see cref="IAvaloniaGpu"/> that draws chrome through EGL on a Basin <see cref="GlDevice"/> into ARGB8888 dmabufs.
/// Create, use and dispose it on one thread.
/// </summary>
public sealed class BasinGlGpu : IAvaloniaGpu, IDisposable
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly GlDevice _device;
    private readonly IAllocator _allocator;
    private readonly BasinGlGraphics _graphics;
    private readonly ulong[] _modifiers;
    private readonly bool _ownsDevice;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="BasinGlGpu"/> class.
    /// </summary>
    /// <param name="device">The GL device that renders and allocates.</param>
    /// <param name="allocator">The allocator for the chrome buffers.</param>
    /// <param name="imports">The formats that the compositor renderer can import, or <see langword="null"/> to allow every format of the device. Only ARGB8888 modifiers are used.</param>
    /// <param name="ownsDevice"><see langword="true"/> to dispose <paramref name="device"/> with this object.</param>
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

    /// <summary>
    /// Makes a GPU on a shared device, or on a new device for a render node.
    /// </summary>
    /// <param name="renderNodePath">The DRM render node to open when <paramref name="shared"/> is <see langword="null"/>.</param>
    /// <param name="shared">A device to share. This object does not dispose it.</param>
    /// <param name="imports">The formats that the compositor renderer can import, or <see langword="null"/>.</param>
    /// <returns>The GPU, or <see langword="null"/> when no device is given or any step fails.</returns>
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

    /// <inheritdoc/>
    public IPlatformGraphics Graphics => _graphics;

    /// <summary>
    /// Creates the buffers of one chrome surface on this device. The caller owns the target and must dispose it.
    /// </summary>
    /// <returns>A target with no buffers.</returns>
    /// <exception cref="ObjectDisposedException">The GPU is disposed.</exception>
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

    /// <summary>
    /// Disposes the EGL contexts, and the device when this object owns it. A second call does nothing.
    /// </summary>
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
