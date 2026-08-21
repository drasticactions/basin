using System.Runtime.InteropServices;
using Mesa.Egl;
using Mesa.Gbm;
using Mesa.Native;
using Silk.NET.OpenGLES;

namespace Basin.Render.Gl;

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
    }

    public string DevicePath { get; }

    public int DrmFd => _deviceFd;

    public DrmFormatSet SampleableFormats { get; }

    public GL Gl => _gl;

    public EglDisplay Egl => _egl;

    public Basin.Render.Gbm.GbmAllocator CreateAllocator(Basin.Diagnostics.FdLedger? ledger = null) =>
        new(_gbm, SampleableFormats, ledger);

    private GlFenceWait? _fenceWait;

    internal GlFenceWait FenceWait => _fenceWait ??= new GlFenceWait(_egl);

    public bool WaitsOnGpu => FenceWait.IsGpuSide;

    public void WaitFence(int syncFileFd) => FenceWait.Wait(syncFileFd);

    public int ExportFence() => FenceWait.Export(_gl);

    public GbmDevice Gbm => _gbm;

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

    public void BindImageToTexture2D(EglImage image) =>
        ImageTargetTexture((uint)TextureTarget.Texture2D, (void*)image.Handle);

    public void ClearErrors()
    {
        for (var i = 0; i < 32 && _gl.GetError() != GLEnum.NoError; i++)
        {
        }
    }

    public void Dispose()
    {
        _gl.Dispose();
        _egl.ReleaseCurrent();
        _context.Dispose();
        _egl.Dispose();
        _gbm.Dispose();
        close(_deviceFd);
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
