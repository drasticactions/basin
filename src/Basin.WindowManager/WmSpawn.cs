using System.ComponentModel;
using System.Diagnostics;

namespace Basin.WindowManager;

public static class WmSpawn
{
    public static string? Run(params string[] argv)
    {
        ArgumentNullException.ThrowIfNull(argv);
        if (argv.Length == 0)
        {
            return "no command";
        }

        try
        {
            var info = new ProcessStartInfo(argv[0]) { UseShellExecute = false };
            for (var i = 1; i < argv.Length; i++)
            {
                info.ArgumentList.Add(argv[i]);
            }

            using var process = Process.Start(info);
            return null;
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            return error.Message;
        }
    }
}
