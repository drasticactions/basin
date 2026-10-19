using System.Runtime.InteropServices;

namespace Basin.Sdl;

internal static unsafe partial class SdlEgl
{
    private const int Extensions = 0x3055;
    private const int BufferAgeExt = 0x313D;

    public static bool HasBufferAge(nint display)
    {
        if (!OperatingSystem.IsLinux() || display == 0)
        {
            return false;
        }

        try
        {
            var text = eglQueryString(display, Extensions);
            return text != 0 && Marshal.PtrToStringUTF8(text) is { } names &&
                   names.Split(' ').Contains("EGL_EXT_buffer_age");
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    public static int BufferAge(nint display, nint surface)
    {
        int age;
        return eglQuerySurface(display, surface, BufferAgeExt, &age) != 0 ? Math.Max(0, age) : 0;
    }

    [DllImport("libEGL.so.1")]
    private static extern nint eglQueryString(nint display, int name);

    [DllImport("libEGL.so.1")]
    private static extern uint eglQuerySurface(nint display, nint surface, int attribute, int* value);
}
