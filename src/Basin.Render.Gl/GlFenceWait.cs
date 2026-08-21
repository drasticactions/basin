using Mesa.Egl;
using Mesa.Native;

namespace Basin.Render.Gl;

internal sealed unsafe class GlFenceWait
{
    private const uint SyncNativeFence = 0x3144;
    private const int SyncNativeFenceFd = 0x3145;
    private const int NoNativeFenceFd = -1;
    private const int None = 0x3038;

    private readonly nint _display;
    private readonly delegate* unmanaged[Cdecl]<nint, uint, int*, nint> _createSync;
    private readonly delegate* unmanaged[Cdecl]<nint, nint, int, int> _waitSync;
    private readonly delegate* unmanaged[Cdecl]<nint, nint, uint> _destroySync;
    private readonly delegate* unmanaged[Cdecl]<nint, nint, int> _dupFenceFd;

    public GlFenceWait(EglDisplay egl)
    {
        _display = egl.Handle;
        if (!egl.HasExtension("EGL_ANDROID_native_fence_sync"))
        {
            return;
        }

        try
        {
            _createSync = (delegate* unmanaged[Cdecl]<nint, uint, int*, nint>)EglProc.Load("eglCreateSyncKHR");
            _waitSync = (delegate* unmanaged[Cdecl]<nint, nint, int, int>)EglProc.Load("eglWaitSyncKHR");
            _destroySync = (delegate* unmanaged[Cdecl]<nint, nint, uint>)EglProc.Load("eglDestroySyncKHR");
            _dupFenceFd = (delegate* unmanaged[Cdecl]<nint, nint, int>)EglProc.Load("eglDupNativeFenceFDANDROID");
        }
        catch (InvalidOperationException)
        {
            _createSync = null;
            _dupFenceFd = null;
        }
    }

    public bool IsGpuSide => _createSync is not null;

    public int Export(Silk.NET.OpenGLES.GL gl)
    {
        if (_createSync is null || _dupFenceFd is null)
        {
            return -1;
        }

        int* attribs = stackalloc int[1] { None };
        var sync = _createSync(_display, SyncNativeFence, attribs);
        if (sync == 0)
        {
            return -1;
        }

        gl.Flush();
        var fd = _dupFenceFd(_display, sync);
        _ = _destroySync(_display, sync);
        return fd;
    }

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int dup(int fd);

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int close(int fd);

    public void Wait(int syncFileFd)
    {
        if (syncFileFd < 0)
        {
            return;
        }

        if (_createSync is not null)
        {
            var duplicate = dup(syncFileFd);
            if (duplicate >= 0)
            {
                int* attribs = stackalloc int[3] { SyncNativeFenceFd, duplicate, None };
                var sync = _createSync(_display, SyncNativeFence, attribs);
                if (sync != 0)
                {
                    _ = _waitSync(_display, sync, 0);
                    _ = _destroySync(_display, sync);
                    return;
                }

                _ = close(duplicate);
            }
        }

        RenderFences.WaitSyncFile(syncFileFd);
    }
}
