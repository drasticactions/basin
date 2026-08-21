using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class ExportDmabufManager : IDisposable
{
    public const int Version = 1;

    [DllImport("libc")]
    private static extern long lseek(int fd, long offset, int whence);

    private const int SeekEnd = 2;

    private readonly WlGlobal _global;
    private readonly IDmabufCapture? _capture;

    public ExportDmabufManager(WlServerDisplay display, IDmabufCapture? capture)
    {
        ArgumentNullException.ThrowIfNull(display);
        _capture = capture;
        _global = display.CreateGlobal(ZwlrExportDmabufManagerV1.Interface, Version, OnBind);
    }

    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZwlrExportDmabufManagerV1Resource(client, version, id);
        manager.CaptureOutput += (_, e) =>
        {
            var frame = new ZwlrExportDmabufFrameV1Resource(client, manager.Version, e.Frame);
            var output = OutputGlobal.FromResource(e.Output)?.Output;
            if (output is null || _capture is null)
            {
                frame.SendCancel(ZwlrExportDmabufFrameV1.CancelReason.Permanent);
                return;
            }

            if (!_capture.TryCurrentFrame(output, out var dmabuf))
            {
                frame.SendCancel(ZwlrExportDmabufFrameV1.CancelReason.Temporary);
                return;
            }

            SendFrame(frame, dmabuf);
        };
    }

    private static void SendFrame(ZwlrExportDmabufFrameV1Resource frame, in DmabufAttributes dmabuf)
    {
        frame.SendFrame(
            (uint)dmabuf.Width,
            (uint)dmabuf.Height,
            0,
            0,
            0,
            ZwlrExportDmabufFrameV1.Flags.Transient,
            (uint)dmabuf.Format,
            (uint)(dmabuf.Modifier >> 32),
            (uint)dmabuf.Modifier,
            (uint)dmabuf.PlaneCount);

        for (var plane = 0; plane < dmabuf.PlaneCount; plane++)
        {
            var fd = dmabuf.Fds[plane];
            var size = lseek(fd, 0, SeekEnd);
            if (size < 0)
            {
                size = (long)dmabuf.Strides[plane] * dmabuf.Height;
            }

            frame.SendObject(
                (uint)plane,
                fd,
                (uint)size,
                dmabuf.Offsets[plane],
                dmabuf.Strides[plane],
                (uint)plane);
        }

        var ticks = System.Diagnostics.Stopwatch.GetTimestamp();
        var seconds = (ulong)(ticks / System.Diagnostics.Stopwatch.Frequency);
        var nanoseconds = (uint)((ticks % System.Diagnostics.Stopwatch.Frequency) * 1_000_000_000 / System.Diagnostics.Stopwatch.Frequency);
        frame.SendReady((uint)(seconds >> 32), (uint)seconds, nanoseconds);
    }
}
