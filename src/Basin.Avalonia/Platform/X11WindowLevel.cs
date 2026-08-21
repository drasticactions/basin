using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Platform;

namespace Basin.Avalonia;

[SupportedOSPlatform("linux")]
internal static class X11WindowLevel
{
    private const string LibX11 = "libX11.so.6";

    private const int ClientMessage = 33;
    private const long SubstructureNotifyMask = 1L << 19;
    private const long SubstructureRedirectMask = 1L << 20;
    private const nint StateRemove = 0;
    private const nint StateAdd = 1;
    private const nint SourceApplication = 1;

    internal static void Apply(IPlatformHandle handle, HostStackingBand band)
    {
        if (handle.HandleDescriptor != "XID" || handle.Handle == 0)
        {
            return;
        }

        var display = XOpenDisplay(null);
        if (display == 0)
        {
            return;
        }

        try
        {
            var state = XInternAtom(display, "_NET_WM_STATE", false);
            var below = XInternAtom(display, "_NET_WM_STATE_BELOW", false);
            if (state == 0 || below == 0)
            {
                return;
            }

            var message = new XClientMessage
            {
                Type = ClientMessage,
                Display = display,
                Window = (nuint)handle.Handle,
                MessageType = state,
                Format = 32,
                Data0 = HostStacking.IsTopmost(band) ? StateRemove : StateAdd,
                Data1 = (nint)below,
                Data3 = SourceApplication,
            };
            _ = XSendEvent(
                display,
                XDefaultRootWindow(display),
                false,
                SubstructureRedirectMask | SubstructureNotifyMask,
                ref message);
            _ = XFlush(display);
        }
        finally
        {
            _ = XCloseDisplay(display);
        }
    }

    [StructLayout(LayoutKind.Sequential, Size = 192)]
    private struct XClientMessage
    {
        public int Type;
        public nuint Serial;
        public int SendEvent;
        public nint Display;
        public nuint Window;
        public nuint MessageType;
        public int Format;
        public nint Data0;
        public nint Data1;
        public nint Data2;
        public nint Data3;
        public nint Data4;
    }

    [DllImport(LibX11)]
    private static extern nint XOpenDisplay([MarshalAs(UnmanagedType.LPUTF8Str)] string? name);

    [DllImport(LibX11)]
    private static extern int XCloseDisplay(nint display);

    [DllImport(LibX11)]
    private static extern nuint XInternAtom(
        nint display, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.I4)] bool onlyIfExists);

    [DllImport(LibX11)]
    private static extern nuint XDefaultRootWindow(nint display);

    [DllImport(LibX11)]
    private static extern int XSendEvent(
        nint display,
        nuint window,
        [MarshalAs(UnmanagedType.I4)] bool propagate,
        long mask,
        ref XClientMessage message);

    [DllImport(LibX11)]
    private static extern int XFlush(nint display);
}
