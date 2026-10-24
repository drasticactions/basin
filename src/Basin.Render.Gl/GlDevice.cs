using System.Runtime.InteropServices;
using Mesa.Egl;
using Mesa.Gbm;
using Mesa.Native;
using Silk.NET.OpenGLES;

namespace Basin.Render.Gl;

/// <summary>
/// Owns an EGL display on GBM and a GLES 3 context on one DRM node. The context is made current in the constructor, and every call must come from that thread.
/// </summary>
public sealed unsafe class GlDevice : IDisposable, IRenderDevice
{
    [DllImport("libc", SetLastError = true)]
    private static extern int open(string path, int flags);

    [DllImport("libc")]
    private static extern int close(int fd);

    private readonly int _deviceFd;
    private readonly GbmDevice _gbm;
    private readonly EglDisplay _egl;
    private readonly EglContext _context;
    private readonly GL _gl;

    internal readonly delegate* unmanaged[Cdecl]<uint, void*, void> ImageTargetTexture;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlDevice"/> class and makes its context current. It throws <see cref="InvalidOperationException"/> when the node cannot be opened.
    /// </summary>
    /// <param name="devicePath">The path of the DRM node, for example a render node.</param>
    public GlDevice(string devicePath = "/dev/dri/renderD128")
    {
        DevicePath = devicePath;
        _deviceFd = open(devicePath, 2 );
        if (_deviceFd < 0)
        {
            throw new InvalidOperationException($"cannot open {devicePath}");
        }

        _gbm = GbmDevice.Create(_deviceFd);
        _egl = EglDisplay.GetPlatformDisplay(_gbm);
        EglDisplay.BindApi(EglApi.OpenGles);
        Span<int> configAttribs =
        [
            Libegl.EGL_SURFACE_TYPE, 0,
            Libegl.EGL_RENDERABLE_TYPE, 0x40 ,
            Libegl.EGL_RED_SIZE, 8,
            Libegl.EGL_GREEN_SIZE, 8,
            Libegl.EGL_BLUE_SIZE, 8,
            Libegl.EGL_ALPHA_SIZE, 8,
        ];
        var configs = _egl.ChooseConfigs(configAttribs);
        Span<int> contextAttribs = [Libegl.EGL_CONTEXT_MAJOR_VERSION, 3];
        _context = _egl.CreateContext(configs[0], null, contextAttribs);
        _context.MakeCurrent();

        _gl = GL.GetApi(new EglProcContext());
        ImageTargetTexture = (delegate* unmanaged[Cdecl]<uint, void*, void>)EglProc.Load("glEGLImageTargetTexture2DOES");
        SampleableFormats = QuerySampleableFormats();
        ImportsLinearYuvPlanes = ImportsLinear(DrmFormat.R8) && ImportsLinear(DrmFormat.Gr88);
    }

    /// <inheritdoc/>
    public string DevicePath { get; }

    /// <inheritdoc/>
    public int DrmFd => _deviceFd;

    /// <summary>
    /// Gets the dmabuf formats and modifiers that EGL can import as 2D textures. Each format also lists the invalid modifier, for a buffer with an implicit modifier.
    /// </summary>
    public DrmFormatSet SampleableFormats { get; }

    /// <summary>
    /// Gets a value indicating whether EGL imports linear R8 and GR88 planes as 2D textures, which the NV12 path needs.
    /// </summary>
    public bool ImportsLinearYuvPlanes { get; }

    /// <summary>
    /// Gets the GLES entry points of the context.
    /// </summary>
    public GL Gl => _gl;

    /// <summary>
    /// Gets the EGL display on the GBM device.
    /// </summary>
    public EglDisplay Egl => _egl;

    /// <summary>
    /// Gets the GLES context. The device owns it.
    /// </summary>
    public EglContext Context => _context;

    /// <summary>
    /// Occurs before a texture is deleted, so that a holder of state for the texture name can drop it. <see cref="NotifyTextureDeleting"/> raises it.
    /// </summary>
    public event Action<uint>? TextureDeleting;

    /// <summary>
    /// Raises <see cref="TextureDeleting"/>. The owner of a texture that an effect or a renderer can sample must call it before it deletes the texture.
    /// </summary>
    /// <param name="texture">The name of the texture that is about to be deleted.</param>
    public void NotifyTextureDeleting(uint texture) => TextureDeleting?.Invoke(texture);

    /// <summary>
    /// Creates an allocator that makes dmabufs through the GBM device of this node. It advertises <see cref="SampleableFormats"/>.
    /// </summary>
    /// <param name="ledger">The ledger that records the fds of each buffer, or <see langword="null"/>.</param>
    /// <returns>The allocator, which the caller disposes before the device.</returns>
    public Basin.Render.Gbm.GbmAllocator CreateAllocator(Basin.Diagnostics.FdLedger? ledger = null) =>
        new(_gbm, SampleableFormats, ledger);

    private GlFenceWait? _fenceWait;

    internal GlFenceWait FenceWait => _fenceWait ??= new GlFenceWait(_egl);

    /// <summary>
    /// Gets a value indicating whether <see cref="WaitFence"/> makes the GPU wait. When it is <see langword="false"/>, a wait blocks the calling thread.
    /// </summary>
    public bool WaitsOnGpu => FenceWait.IsGpuSide;

    /// <summary>
    /// Makes later GL commands wait for a fence. When EGL cannot wait on the GPU, it blocks the calling thread until the fence signals.
    /// </summary>
    /// <param name="syncFileFd">A sync_file fd that the caller keeps, or a negative value for no wait.</param>
    public void WaitFence(int syncFileFd) => FenceWait.Wait(syncFileFd);

    /// <summary>
    /// Flushes the context and exports a fence that signals when the GPU finished all earlier commands.
    /// </summary>
    /// <returns>A sync_file fd that the caller owns, or -1 when EGL cannot export one.</returns>
    public int ExportFence() => FenceWait.Export(_gl);

    /// <summary>
    /// Gets the GBM device on the DRM node. The device owns it.
    /// </summary>
    public GbmDevice Gbm => _gbm;

    /// <summary>
    /// Imports a dmabuf as an EGL image. The invalid modifier imports without explicit modifier attributes.
    /// </summary>
    /// <param name="attributes">The dmabuf to import. Its fds stay owned by the caller.</param>
    /// <returns>The image, which the caller disposes, or <see langword="null"/> when EGL rejects the dmabuf.</returns>
    public EglImage? ImportDmabufImage(in DmabufAttributes attributes)
    {
        Span<nint> attribs = stackalloc nint[7 + attributes.PlaneCount * 10];
        var n = 0;
        attribs[n++] = EglDmabuf.Width;
        attribs[n++] = attributes.Width;
        attribs[n++] = EglDmabuf.Height;
        attribs[n++] = attributes.Height;
        attribs[n++] = EglDmabuf.Fourcc;
        attribs[n++] = (nint)(uint)attributes.Format;
        var withModifier = attributes.Modifier != DrmFormatSet.ModifierInvalid;
        for (var plane = 0; plane < attributes.PlaneCount; plane++)
        {
            attribs[n++] = EglDmabuf.PlaneFd[plane];
            attribs[n++] = attributes.Fds[plane];
            attribs[n++] = EglDmabuf.PlaneOffset[plane];
            attribs[n++] = (nint)attributes.Offsets[plane];
            attribs[n++] = EglDmabuf.PlanePitch[plane];
            attribs[n++] = (nint)attributes.Strides[plane];
            if (withModifier)
            {
                attribs[n++] = EglDmabuf.PlaneModifierLo[plane];
                attribs[n++] = (nint)(uint)(attributes.Modifier & 0xFFFFFFFF);
                attribs[n++] = EglDmabuf.PlaneModifierHi[plane];
                attribs[n++] = (nint)(uint)(attributes.Modifier >> 32);
            }
        }

        try
        {
            return _egl.CreateImage(null, EglDmabuf.LinuxDmaBufExt, IntPtr.Zero, attribs[..n]);
        }
        catch (EglException)
        {
            return null;
        }
    }

    /// <summary>
    /// Attaches an EGL image to the 2D texture that is bound now, through glEGLImageTargetTexture2DOES.
    /// </summary>
    /// <param name="image">The image to attach. It must outlive the texture.</param>
    public void BindImageToTexture2D(EglImage image) =>
        ImageTargetTexture((uint)TextureTarget.Texture2D, (void*)image.Handle);

    /// <summary>
    /// Reads and drops pending GL errors, at most 32, so that a later error check sees only new errors.
    /// </summary>
    public void ClearErrors()
    {
        for (var i = 0; i < 32 && _gl.GetError() != GLEnum.NoError; i++)
        {
        }
    }

    /// <summary>
    /// Releases the context, destroys the context, the display and the GBM device, and closes the DRM node fd.
    /// </summary>
    public void Dispose()
    {
        _gl.Dispose();
        _egl.ReleaseCurrent();
        _context.Dispose();
        _egl.Dispose();
        _gbm.Dispose();
        close(_deviceFd);
    }

    private bool ImportsLinear(DrmFormat format)
    {
        var queryModifiers = (delegate* unmanaged[Cdecl]<void*, int, int, ulong*, uint*, int*, uint>)EglProc.Load("eglQueryDmaBufModifiersEXT");
        var fourcc = (int)(uint)format;
        var count = 0;
        if (queryModifiers((void*)_egl.Handle, fourcc, 0, null, null, &count) == 0 || count == 0)
        {
            return false;
        }

        var modifiers = new ulong[count];
        var external = new uint[count];
        fixed (ulong* modifiersPtr = modifiers)
        fixed (uint* externalPtr = external)
        {
            queryModifiers((void*)_egl.Handle, fourcc, count, modifiersPtr, externalPtr, &count);
        }

        for (var i = 0; i < count; i++)
        {
            if (modifiers[i] == DrmFormatSet.ModifierLinear && external[i] == 0)
            {
                return true;
            }
        }

        return false;
    }

    private DrmFormatSet QuerySampleableFormats()
    {
        var set = new DrmFormatSet();
        var queryFormats = (delegate* unmanaged[Cdecl]<void*, int, int*, int*, uint>)EglProc.Load("eglQueryDmaBufFormatsEXT");
        var queryModifiers = (delegate* unmanaged[Cdecl]<void*, int, int, ulong*, uint*, int*, uint>)EglProc.Load("eglQueryDmaBufModifiersEXT");

        var count = 0;
        queryFormats((void*)_egl.Handle, 0, null, &count);
        var formats = new int[count];
        fixed (int* formatsPtr = formats)
        {
            queryFormats((void*)_egl.Handle, count, formatsPtr, &count);
        }

        foreach (var fourcc in formats)
        {
            var format = (DrmFormat)(uint)fourcc;
            if (!PixelFormatInfo.TryGet(format, out _))
            {
                continue;
            }

            var modifierCount = 0;
            queryModifiers((void*)_egl.Handle, fourcc, 0, null, null, &modifierCount);
            var modifiers = new ulong[modifierCount];
            var external = new uint[Math.Max(modifierCount, 1)];
            fixed (ulong* modifiersPtr = modifiers)
            fixed (uint* externalPtr = external)
            {
                queryModifiers((void*)_egl.Handle, fourcc, modifierCount, modifiersPtr, externalPtr, &modifierCount);
            }

            for (var i = 0; i < modifierCount; i++)
            {
                if (external[i] == 0)
                {
                    set.Add(format, modifiers[i]);
                }
            }

            set.Add(format, DrmFormatSet.ModifierInvalid);
        }

        return set;
    }
}
