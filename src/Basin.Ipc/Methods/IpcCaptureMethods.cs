using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin.Ipc;

internal static class IpcCaptureMethods
{
    private const int InlineHeadroom = 4096;

    public static void Register(IpcServer server, IpcDescribe describe)
    {
        if (describe.Capture is not { } capture)
        {
            return;
        }

        var methods = server.Methods;
        methods.RegisterLibrary(IpcMethodNames.CaptureOutput, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcCaptureOutputParams) is not { } request)
            {
                return;
            }

            IOutput? output;
            if (request.Output is { } name)
            {
                output = describe.OutputNamed(name);
                if (output is null)
                {
                    reply.Error(IpcErrorCodes.NotFound, $"no output '{name}'");
                    return;
                }
            }
            else
            {
                output = describe.PointerOutput() ?? (describe.AllOutputs() is { Count: > 0 } all ? all[0] : null);
                if (output is null)
                {
                    reply.Error(IpcErrorCodes.NotFound, "this session has no output");
                    return;
                }
            }

            Capture(reply, capture, CaptureSource.Output(output, request.Cursor == true), request.To, request.Scale, request.MaxDimension);
        });

        methods.RegisterLibrary(IpcMethodNames.CaptureWindow, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcCaptureWindowParams) is not { } request)
            {
                return;
            }

            if (describe.Toplevels is { } model && !model.TryGet(request.Id, out _))
            {
                reply.Error(IpcErrorCodes.NotFound, $"no window {request.Id}");
                return;
            }

            Capture(
                reply,
                capture,
                CaptureSource.Toplevel(request.Id, request.ClientOnly == true, request.Cursor == true),
                request.To,
                request.Scale,
                request.MaxDimension);
        });

        methods.RegisterLibrary(IpcMethodNames.CaptureRegion, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcCaptureRegionParams) is not { } request)
            {
                return;
            }

            if (request.Width <= 0 || request.Height <= 0 || request.Width > 32768 || request.Height > 32768)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "width and height are between 1 and 32768");
                return;
            }

            var box = new Box(request.X, request.Y, request.Width, request.Height);
            Capture(reply, capture, CaptureSource.Region(box, overlayCursor: request.Cursor == true), request.To, request.Scale, request.MaxDimension);
        });
    }

    private static void Capture(
        IpcReply reply, IScreenCapture capture, in CaptureSource source, IpcCaptureTo to, double? requestedScale, long? maxDimension)
    {
        if (to.Kind == IpcCaptureKind.Path && !Path.IsPathRooted(to.Path))
        {
            reply.Error(IpcErrorCodes.InvalidParams, "'path' is absolute; the compositor's working directory is not yours");
            return;
        }

        var scale = requestedScale ?? 1.0;
        if (scale <= 0 || scale > 1)
        {
            reply.Error(IpcErrorCodes.InvalidParams, "'scale' is above 0 and at most 1");
            return;
        }

        if (maxDimension is < 1)
        {
            reply.Error(IpcErrorCodes.InvalidParams, "'max_dimension' is at least 1");
            return;
        }

        if (requestedScale is not null && maxDimension is not null)
        {
            reply.Error(IpcErrorCodes.InvalidParams, "name 'scale' or 'max_dimension', not both");
            return;
        }

        if (!capture.Supports(source))
        {
            reply.Error(IpcErrorCodes.Unavailable, "the compositor cannot capture that source");
            return;
        }

        if (!capture.TryDescribe(source, out var format) || format.Width <= 0 || format.Height <= 0)
        {
            reply.Error(IpcErrorCodes.Failed, "the source has nothing to capture");
            return;
        }

        var buffer = new MemoryBuffer(format.Width, format.Height, format.Format);
        try
        {
            if (!capture.Capture(source, new Box(0, 0, format.Width, format.Height), buffer))
            {
                reply.Error(IpcErrorCodes.Failed, "the capture did not complete");
                return;
            }

            var size = OutputSize(buffer.Width, buffer.Height, scale, maxDimension ?? 0);
            switch (to.Kind)
            {
                case IpcCaptureKind.Fd:
                    WriteFd(reply, buffer);
                    break;
                case IpcCaptureKind.Path:
                    WritePath(reply, buffer, to.Path!, size);
                    break;
                default:
                    WriteInline(reply, buffer, size);
                    break;
            }
        }
        finally
        {
            buffer.Destroy();
        }
    }

    private static unsafe void WriteFd(IpcReply reply, MemoryBuffer buffer)
    {
        if (!buffer.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            reply.Error(IpcErrorCodes.Failed, "the capture buffer has no CPU mapping");
            return;
        }

        int fd;
        try
        {
            fd = IpcMemfd.Create("basin-ipc-capture", new ReadOnlySpan<byte>((void*)view.Data, view.Stride * buffer.Height));
        }
        finally
        {
            buffer.EndDataAccess();
        }

        if (fd < 0)
        {
            reply.Error(IpcErrorCodes.Failed, $"memfd_create failed (errno {UnixSocket.LastError})");
            return;
        }

        reply.Write(
            new IpcCaptureResult(buffer.Width, buffer.Height, Fd: reply.AttachFd(fd), Format: view.Format.ToString().ToLowerInvariant(), Stride: view.Stride),
            IpcJsonContext.Default.IpcCaptureResult);
    }

    private static void WritePath(IpcReply reply, MemoryBuffer buffer, string path, (int Width, int Height) size)
    {
        var (rgba, width, height) = Scaled(BufferCapture.ReadRgba(buffer), buffer.Width, buffer.Height, size);
        try
        {
            File.WriteAllBytes(path, PngCodec.Encode(rgba, width, height));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            reply.Error(IpcErrorCodes.Failed, exception.Message);
            return;
        }

        reply.Write(new IpcCaptureResult(width, height, Path: path), IpcJsonContext.Default.IpcCaptureResult);
    }

    private static void WriteInline(IpcReply reply, MemoryBuffer buffer, (int Width, int Height) size)
    {
        var (rgba, width, height) = Scaled(BufferCapture.ReadRgba(buffer), buffer.Width, buffer.Height, size);
        var png = PngCodec.Encode(rgba, width, height);
        if (((png.Length + 2) / 3 * 4) + InlineHeadroom > IpcProtocol.MaxMessageBytes)
        {
            reply.Error(IpcErrorCodes.TooLarge, $"a {png.Length}-byte PNG does not fit in a frame; ask for a path or an fd");
            return;
        }

        reply.Write(new IpcCaptureResult(width, height, Png: png), IpcJsonContext.Default.IpcCaptureResult);
    }

    internal static (int Width, int Height) OutputSize(int width, int height, double scale, long maxDimension)
    {
        if (maxDimension > 0)
        {
            var longer = Math.Max(width, height);
            if (longer <= maxDimension)
            {
                return (width, height);
            }

            return width >= height
                ? ((int)maxDimension, Math.Max(1, (int)Math.Round((double)height * maxDimension / width)))
                : (Math.Max(1, (int)Math.Round((double)width * maxDimension / height)), (int)maxDimension);
        }

        if (scale >= 1)
        {
            return (width, height);
        }

        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static (byte[] Rgba, int Width, int Height) Scaled(byte[] rgba, int width, int height, (int Width, int Height) size)
    {
        if (size.Width >= width && size.Height >= height)
        {
            return (rgba, width, height);
        }

        var (outWidth, outHeight) = size;
        var result = new byte[outWidth * outHeight * 4];
        for (var y = 0; y < outHeight; y++)
        {
            var y0 = y * height / outHeight;
            var y1 = Math.Max(y0 + 1, (y + 1) * height / outHeight);
            for (var x = 0; x < outWidth; x++)
            {
                var x0 = x * width / outWidth;
                var x1 = Math.Max(x0 + 1, (x + 1) * width / outWidth);
                int r = 0, g = 0, b = 0, a = 0;
                for (var sy = y0; sy < y1; sy++)
                {
                    for (var sx = x0; sx < x1; sx++)
                    {
                        var i = ((sy * width) + sx) * 4;
                        r += rgba[i];
                        g += rgba[i + 1];
                        b += rgba[i + 2];
                        a += rgba[i + 3];
                    }
                }

                var count = (y1 - y0) * (x1 - x0);
                var o = ((y * outWidth) + x) * 4;
                result[o] = (byte)(r / count);
                result[o + 1] = (byte)(g / count);
                result[o + 2] = (byte)(b / count);
                result[o + 3] = (byte)(a / count);
            }
        }

        return (result, outWidth, outHeight);
    }
}
