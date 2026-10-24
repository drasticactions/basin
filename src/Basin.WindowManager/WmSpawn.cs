using System.ComponentModel;
using System.Diagnostics;

namespace Basin.WindowManager;

/// <summary>
/// Starts commands for a window manager without a shell.
/// </summary>
public static class WmSpawn
{
    /// <summary>
    /// Starts a program with arguments and does not wait for it. It returns the failure reason in place of a log line, so the caller picks the category and level.
    /// </summary>
    /// <param name="argv">The program, then its arguments. No shell expands them.</param>
    /// <returns><see langword="null"/> when the process started, or the reason it did not.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="argv"/> is <see langword="null"/>.</exception>
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
