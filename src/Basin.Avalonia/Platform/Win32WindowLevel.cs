using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Platform;

namespace Basin.Avalonia;

[SupportedOSPlatform("windows")]
internal static class Win32WindowLevel
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    private static readonly nint HwndTopmost = -1;
    private static readonly nint HwndBottom = 1;

    internal static void Apply(IPlatformHandle handle, HostStackingBand band, bool takesKeyboard)
    {
        var hwnd = handle.Handle;
        if (hwnd == 0)
        {
            return;
        }

        if (HostStacking.IsTopmost(band))
        {
            if (band == HostStackingBand.Overlay)
            {
                _ = SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
            }

            return;
        }

        if (!takesKeyboard)
        {
            var style = GetWindowLongPtrW(hwnd, GwlExStyle);
            _ = SetWindowLongPtrW(hwnd, GwlExStyle, style | WsExNoActivate);
        }

        _ = SetWindowPos(hwnd, HwndBottom, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetWindowLongPtrW(nint hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);
}
