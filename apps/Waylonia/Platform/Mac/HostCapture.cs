using System.Runtime.InteropServices;
using Basin.Avalonia;
using static Waylonia.WayloniaLog;

namespace Waylonia;

internal sealed unsafe class HostCapture : IDisposable
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const uint SessionEventTap = 1;
    private const uint HeadInsertEventTap = 0;
    private const uint EventTapOptionDefault = 0;
    private const uint EventKeyDown = 10;
    private const uint EventKeyUp = 11;
    private const uint KeyboardEventKeycode = 9;
    private const ulong CommandFlag = 0x00100000;

    private const ushort KeyTab = 48;
    private const ushort KeySpace = 49;

    private static readonly ushort[] FunctionRow =
        [122, 120, 99, 118, 96, 97, 98, 100, 101, 109, 103, 111];

    private IntPtr _tap;
    private IntPtr _source;
    private bool _disposed;

    public static IDisposable? TryGrab(
        global::Avalonia.Controls.TopLevel anchor,
        BasinOutputView view,
        BasinCompositorHost host,
        CaptureHooks hooks)
    {
        var mask = (1UL << (int)EventKeyDown) | (1UL << (int)EventKeyUp);
        var tap = CGEventTapCreate(
            SessionEventTap, HeadInsertEventTap, EventTapOptionDefault, mask, &OnEvent, IntPtr.Zero);
        if (tap == IntPtr.Zero)
        {
            Log.Warn($"the event tap was refused: Waylonia needs Accessibility permission to grab the host. " +
                $"Command+Tab, Command+Space and the function row stay with macOS while the desktop is captured");
            return null;
        }

        var source = CFMachPortCreateRunLoopSource(IntPtr.Zero, tap, IntPtr.Zero);
        CFRunLoopAddSource(CFRunLoopGetCurrent(), source, GetCommonModes());
        CGEventTapEnable(tap, true);
        return new HostCapture { _tap = tap, _source = source };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_tap != IntPtr.Zero)
        {
            CGEventTapEnable(_tap, false);
        }

        if (_source != IntPtr.Zero)
        {
            CFRunLoopRemoveSource(CFRunLoopGetCurrent(), _source, GetCommonModes());
            CFRelease(_source);
            _source = IntPtr.Zero;
        }

        if (_tap != IntPtr.Zero)
        {
            CFRelease(_tap);
            _tap = IntPtr.Zero;
        }
    }

    [UnmanagedCallersOnly]
    private static IntPtr OnEvent(IntPtr proxy, uint type, IntPtr eventRef, IntPtr userInfo)
    {
        try
        {
            var keycode = (ushort)CGEventGetIntegerValueField(eventRef, KeyboardEventKeycode);
            var flags = CGEventGetFlags(eventRef);
            if ((flags & CommandFlag) != 0 && keycode is KeyTab or KeySpace)
            {
                return IntPtr.Zero;
            }

            foreach (var candidate in FunctionRow)
            {
                if (candidate == keycode)
                {
                    return IntPtr.Zero;
                }
            }
        }
        catch (Exception)
        {
            return eventRef;
        }

        return eventRef;
    }

    private static IntPtr GetCommonModes() => Marshal.ReadIntPtr(
        CFGetSymbol("kCFRunLoopCommonModes"));

    private static IntPtr CFGetSymbol(string name)
    {
        var handle = NativeLibrary.Load(CoreFoundation);
        return NativeLibrary.GetExport(handle, name);
    }

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGEventTapCreate(
        uint tap,
        uint place,
        uint options,
        ulong eventsOfInterest,
        delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr> callback,
        IntPtr userInfo);

    [DllImport(CoreGraphics)]
    private static extern void CGEventTapEnable(IntPtr tap, [MarshalAs(UnmanagedType.I1)] bool enable);

    [DllImport(CoreGraphics)]
    private static extern long CGEventGetIntegerValueField(IntPtr eventRef, uint field);

    [DllImport(CoreGraphics)]
    private static extern ulong CGEventGetFlags(IntPtr eventRef);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr allocator, IntPtr port, IntPtr order);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFRunLoopGetCurrent();

    [DllImport(CoreFoundation)]
    private static extern void CFRunLoopAddSource(IntPtr loop, IntPtr source, IntPtr mode);

    [DllImport(CoreFoundation)]
    private static extern void CFRunLoopRemoveSource(IntPtr loop, IntPtr source, IntPtr mode);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr reference);
}
