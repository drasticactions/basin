using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Cli;

/// <summary>
/// Formats the status lines that a compositor writes on standard output for scripts and tests to match.
/// </summary>
public static class CompositorLines
{
    /// <summary>
    /// Formats the <c>SOCKET</c> line that names the Wayland socket.
    /// </summary>
    /// <param name="name">The socket name, or <see langword="null"/> or empty when the compositor inherited its socket.</param>
    /// <returns><c>SOCKET</c> followed by the name, or by <c>(inherited)</c>.</returns>
    public static string Socket(string? name) =>
        string.IsNullOrEmpty(name) ? "SOCKET (inherited)" : $"SOCKET {name}";

    /// <summary>
    /// Formats the <c>FRAMES</c> line with the rendered frame count and the live tracked object count.
    /// </summary>
    /// <param name="rendered">The number of frames that the run rendered.</param>
    /// <returns>The line. The live count is <c>untracked</c> when lifetime tracking is not compiled in.</returns>
    public static string Frames(long rendered) =>
        $"FRAMES {rendered} LIVE {(BasinCounters.Enabled ? BasinCounters.LiveObjects.ToString() : "untracked")}";

    /// <summary>
    /// Gets the census report of tracked objects that are still live or waiting to be freed.
    /// </summary>
    /// <returns>The report, or <see langword="null"/> when tracking is off or nothing is outstanding.</returns>
    public static string? Census() =>
        BasinCounters.Enabled && (BasinCounters.LiveObjects != 0 || BasinCounters.PendingFrees != 0)
            ? BasinCounters.CensusReport()
            : null;

    /// <summary>
    /// Formats the <c>COLOR</c> line with the number of attached color lookup tables.
    /// </summary>
    /// <param name="attached">The number of attached lookup tables.</param>
    /// <returns>The line.</returns>
    public static string Luts(int attached) => $"COLOR luts={attached}";

    /// <summary>
    /// Formats the <c>SCANOUT</c> line that tells which kind of buffer an output scans out from.
    /// </summary>
    /// <param name="output">The output name.</param>
    /// <param name="choice">The buffer kind that the output uses.</param>
    /// <param name="modifiers">The number of format modifiers that the plane offers. The line includes it only for device buffers.</param>
    /// <returns>The line.</returns>
    public static string Scanout(string output, ScanoutChoice choice, int modifiers) => choice switch
    {
        ScanoutChoice.DeviceBuffers => $"SCANOUT {output} device modifiers={modifiers}",
        ScanoutChoice.DumbLinear => $"SCANOUT {output} dumb linear; every frame reads the framebuffer back",
        _ => $"SCANOUT {output} device buffers refused by the plane; falling back to dumb linear",
    };

    /// <summary>
    /// Formats the <c>SHOTRAW</c> line that reports the result of a raw screenshot.
    /// </summary>
    /// <param name="outcome">The result of the screenshot.</param>
    /// <param name="buffer">The captured buffer. It must not be <see langword="null"/> unless <paramref name="outcome"/> is <see cref="ScreenshotOutcome.NoFrame"/>.</param>
    /// <param name="path">The file that the screenshot went to.</param>
    /// <returns>The line, with the buffer size in pixels when there is a buffer.</returns>
    public static string ShotRaw(ScreenshotOutcome outcome, IBuffer? buffer, string path) => outcome switch
    {
        ScreenshotOutcome.NoFrame => "SHOTRAW none",
        ScreenshotOutcome.Unreadable => $"SHOTRAW unreadable {buffer!.Width}x{buffer.Height}",
        _ => $"SHOTRAW {path} {buffer!.Width}x{buffer.Height}",
    };
}
