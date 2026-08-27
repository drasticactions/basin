using System.Runtime.InteropServices;
using Basin.Avalonia;
using static Waylonia.WayloniaLog;

namespace Waylonia;

internal sealed unsafe class HostCapture : IDisposable
{
    private const int KeyboardHook = 13;
    private const uint VkTab = 0x09;
    private const uint VkEscape = 0x1B;
    private const uint VkLeftWin = 0x5B;
    private const uint VkRightWin = 0x5C;
    private const uint AltDown = 0x20;

    private static IntPtr _hook;
    private static IntPtr _foreground;

    private bool _disposed;

    public static IDisposable? TryGrab(
        global::Avalonia.Controls.TopLevel anchor,
        BasinOutputView view,
        BasinCompositorHost host,
        CaptureHooks hooks)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        if (anchor.TryGetPlatformHandle() is not { } handle)
        {
            Log.Warn($"the desktop window has no Win32 handle, the host keeps its own chords");
            return null;
        }

        _foreground = handle.Handle;
        _hook = SetWindowsHookExW(KeyboardHook, &OnKey, IntPtr.Zero, 0);
        if (_hook == IntPtr.Zero)
        {
            Log.Warn($"the low-level keyboard hook was refused ({Marshal.GetLastPInvokeError()}), " +
                $"the host keeps its own chords");
            return null;
        }

        Log.Debug($"the Windows key and Alt+Tab go to the desktop while it is captured; " +
            $"Ctrl+Alt+Del is never grabbable and is not attempted");
        return new HostCapture();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hook != IntPtr.Zero)
        {
            _ = UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [UnmanagedCallersOnly]
    private static IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0 && GetForegroundWindow() == _foreground)
            {
                var info = *(KeyboardHookStruct*)lParam;
                var swallow = info.VkCode is VkLeftWin or VkRightWin
                    || (info.VkCode is VkTab or VkEscape && (info.Flags & AltDown) != 0);
                if (swallow)
                {
                    return 1;
                }
            }
        }
        catch (Exception)
        {
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(
        int idHook, delegate* unmanaged<int, IntPtr, IntPtr, IntPtr> lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
