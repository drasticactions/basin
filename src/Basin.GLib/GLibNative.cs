using System.Runtime.InteropServices;

namespace Basin.GLib;

internal static unsafe class GLibNative
{
    public const string Library = "libglib-2.0.so.0";

    public const ushort In = 1;
    public const ushort Pri = 2;
    public const ushort Out = 4;
    public const ushort Err = 8;
    public const ushort Hup = 16;
    public const ushort Nval = 32;

    [DllImport(Library)]
    public static extern nint g_main_context_default();

    [DllImport(Library)]
    public static extern int g_main_context_acquire(nint context);

    [DllImport(Library)]
    public static extern void g_main_context_release(nint context);

    [DllImport(Library)]
    public static extern int g_main_context_prepare(nint context, int* priority);

    [DllImport(Library)]
    public static extern int g_main_context_query(nint context, int maxPriority, int* timeout, GPollFd* fds, int count);

    [DllImport(Library)]
    public static extern int g_main_context_check(nint context, int maxPriority, GPollFd* fds, int count);

    [DllImport(Library)]
    public static extern void g_main_context_dispatch(nint context);

    [DllImport("libc", SetLastError = true)]
    public static extern int poll(GPollFd* fds, nuint count, int timeout);

    [DllImport("libc")]
    public static extern int gettid();
}
