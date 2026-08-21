using System.Runtime.InteropServices;
using Mesa.Egl;
using Mesa.Gbm;
using Mesa.Native;
using Silk.NET.OpenGLES;

namespace Basin.Render.Gl;

internal sealed class EglProcContext : Silk.NET.Core.Contexts.INativeContext
{
    public nint GetProcAddress(string proc, int? slot = null)
    {
        TryGetProcAddress(proc, out var address, slot);
        return address;
    }

    public unsafe bool TryGetProcAddress(string proc, out nint address, int? slot = null)
    {
        var name = Marshal.StringToCoTaskMemUTF8(proc);
        try
        {
            address = (nint)Libegl.eglGetProcAddress((sbyte*)name);
        }
        finally
        {
            Marshal.FreeCoTaskMem(name);
        }

        return address != 0;
    }

    public void Dispose()
    {
    }
}
