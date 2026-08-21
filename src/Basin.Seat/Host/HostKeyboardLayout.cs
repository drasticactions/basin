using System.Text;

namespace Basin.Seat;

public static class HostKeyboardLayout
{
    public static IHostKeyboardLayout? Detect()
    {
        if (OperatingSystem.IsMacOS())
        {
            return MacKeyboardLayout.TryCreate();
        }

        if (OperatingSystem.IsWindows())
        {
            return WindowsKeyboardLayout.TryCreate();
        }

        return null;
    }

    public static string FallbackKeymapText => HostKeymapWriter.Fallback;
}
