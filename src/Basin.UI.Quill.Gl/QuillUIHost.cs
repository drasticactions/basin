using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Gl;
using Prowl.Quill;

namespace Basin.UI.Quill;

public sealed class QuillUIHost : IUIHost
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly GlDevice _device;
    private readonly IAllocator _allocator;
    private readonly FontAtlasSettings _atlas;
    private readonly QuillGlContext _context;
    private readonly QuillGlProgram _program;
    private readonly QuillTextures _textures;
    private readonly List<QuillUISurface> _surfaces = [];
    private readonly ulong[] _modifiers;
    private readonly bool _ownsDevice;
    private readonly bool _ownsAllocator;
    private bool _syncReported;
    private bool _disposed;

    public QuillUIHost(GlDevice device, IAllocator allocator, FontAtlasSettings? atlas = null)
        : this(device, allocator, [], ownsDevice: false, ownsAllocator: false, atlas)
    {
    }

    private QuillUIHost(
        GlDevice device, IAllocator allocator, ulong[] modifiers, bool ownsDevice, bool ownsAllocator,
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
        _context = new QuillGlContext(device, switches: ownsDevice);
        using var current = _context.Enter();
        _program = new QuillGlProgram(device.Gl);
        _textures = new QuillTextures(device.Gl, _context);
    }

    public static QuillUIHost? TryCreate(
        GlDevice? shared,
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
            declined = "the renderer has no GlDevice to share and no render node was named for one of its own";
            return null;
        }

        if (imports is not null && !imports.Contains(DrmFormat.Argb8888))
        {
            declined = "the renderer imports no Argb8888 dmabuf";
            return null;
        }

        var saved = QuillGlContext.Save();
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
        var host = TryBuild(device, ownsDevice: true, imports, allocator: null, atlas, out declined);
        if (host is null)
        {
            DisposeOwned(device);
        }

        return host;
    }

    public bool OwnsDevice => _ownsDevice;

    public GlDevice Device => _device;

    internal int FencedFrames { get; private set; }

    internal int FinishedFrames { get; private set; }

    internal ReadOnlySpan<ulong> Modifiers => _modifiers;

    internal QuillGlContext Context => _context;

    public Type SurfaceContract => typeof(IQuillUISurface);

    public UITargetKind Produces => UITargetKind.Dmabuf;

    public QuillTextures Textures
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
        using (_context.Enter())
        {
            _textures.Dispose();
            _program.Dispose();
        }

        if (_ownsAllocator)
        {
            _allocator.Dispose();
        }

        if (_ownsDevice)
        {
            DisposeOwned(_device);
        }
    }

    internal QuillUISurface Create()
    {
        var surface = new QuillUISurface(_device, _allocator, _program, _textures, _atlas, this);
        _surfaces.Add(surface);
        return surface;
    }

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
            QuillLog.Log.Debug($"chrome on {_device.DevicePath} publishes a native fence to each dmabuf it draws");
        }
        else
        {
            QuillLog.Log.Debug($"chrome on {_device.DevicePath} exports no native fence and waits on glFinish for each frame");
        }
    }

    private static QuillUIHost? TryBuild(
        GlDevice device, bool ownsDevice, DrmFormatSet? imports, IAllocator? allocator, FontAtlasSettings? atlas,
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

        try
        {
            declined = null;
            return new QuillUIHost(device, allocator, modifiers, ownsDevice, ownsAllocator, atlas);
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

        using var current = new QuillGlContext(device, ownsDevice).Enter();
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

    private static void DisposeOwned(GlDevice device)
    {
        var saved = QuillGlContext.Save();
        device.Dispose();
        saved.Dispose();
    }

    internal void Forget(QuillUISurface surface) => _surfaces.Remove(surface);
}
