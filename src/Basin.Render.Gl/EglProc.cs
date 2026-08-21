using System.Runtime.InteropServices;
using Mesa.Egl;
using Mesa.Gbm;
using Mesa.Native;
using Silk.NET.OpenGLES;

namespace Basin.Render.Gl;

internal static unsafe class EglProc
{
    public static void* Load(string name)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* bytesPtr = bytes)
        {
            var proc = Libegl.eglGetProcAddress((sbyte*)bytesPtr);
            if (proc is null)
            {
                throw new InvalidOperationException($"{name} unavailable");
            }

            return proc;
        }
    }
}
