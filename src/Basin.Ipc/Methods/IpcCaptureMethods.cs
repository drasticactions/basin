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
                    reply.Error(IpcErrorCodes.NotFound, $"No output has the name '{name}'. Call outputs/list for the outputs.");
                    return;
                }
            }
            else
            {
                output = describe.PointerOutput() ?? (describe.AllOutputs() is { Count: > 0 } all ? all[0] : null);
                if (output is null)
                {
                    reply.Error(IpcErrorCodes.NotFound, "This session has no output.");
                    return;
                }
            }

            Capture(reply, capture, CaptureSource.Output(output, request.Cursor == true), request.To, request.Scale, request.MaxDimension, request.Format);
        });

        methods.RegisterLibrary(IpcMethodNames.CaptureWindow, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcCaptureWindowParams) is not { } request)
            {
                return;
            }

            if (describe.Toplevels is { } model && !model.TryGet(request.Id, out _))
            {
                reply.Error(IpcErrorCodes.NotFound, $"No window has the id {request.Id}. Call windows/list for the windows.");
                return;
            }

            Capture(
                reply,
                capture,
                CaptureSource.Toplevel(request.Id, request.ClientOnly == true, request.Cursor == true),
                request.To,
                request.Scale,
                request.MaxDimension,
                request.Format);
        });

        methods.RegisterLibrary(IpcMethodNames.CaptureRegion, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcCaptureRegionParams) is not { } request)
            {
                return;
            }

            if (request.Width <= 0 || request.Height <= 0 || request.Width > 32768 || request.Height > 32768)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "'width' and 'height' must be between 1 and 32768.");
                return;
            }

            var box = new Box(request.X, request.Y, request.Width, request.Height);
            Capture(reply, capture, CaptureSource.Region(box, overlayCursor: request.Cursor == true), request.To, request.Scale, request.MaxDimension, request.Format);
        });
    }

    private static void Capture(
        IpcReply reply, IScreenCapture capture, in CaptureSource source, IpcCaptureTo to, double? requestedScale, long? maxDimension, string? imageFormat)
    {
        var wide = imageFormat switch
        {
            null or "png" => false,
            "png16" => true,
            _ => (bool?)null,
        };
        if (wide is not { } sixteen)
        {
            reply.Error(IpcErrorCodes.InvalidParams, $"'format' must be png or png16, not '{imageFormat}'.");
            return;
        }

        if (to.Kind == IpcCaptureKind.Path && !Path.IsPathRooted(to.Path))
        {
            reply.Error(IpcErrorCodes.InvalidParams, "'path' must be absolute. The compositor does not share your working directory.");
            return;
        }

        var scale = requestedScale ?? 1.0;
        if (scale <= 0 || scale > 1)
        {
            reply.Error(IpcErrorCodes.InvalidParams, "'scale' must be above 0 and at most 1.");
            return;
        }

        if (maxDimension is < 1)
        {
            reply.Error(IpcErrorCodes.InvalidParams, "'max_dimension' must be at least 1.");
            return;
        }

        if (requestedScale is not null && maxDimension is not null)
        {
            reply.Error(IpcErrorCodes.InvalidParams, "Give 'scale' or 'max_dimension', not both.");
            return;
        }

        if (!capture.Supports(source))
        {
            reply.Error(IpcErrorCodes.Unavailable, "The compositor cannot capture that source.");
            return;
        }

        if (!capture.TryDescribe(source, out var format) || format.Width <= 0 || format.Height <= 0)
        {
            reply.Error(IpcErrorCodes.Failed, "The source has nothing to capture.");
            return;
        }

        if (sixteen && !capture.CanCaptureHalfFloat)
        {
            reply.Error(IpcErrorCodes.Unavailable, $"The png16 format needs a 16-bit render target, and {capture.RendererName ?? "this compositor's renderer"} cannot render one. Ask for png instead.");
            return;
        }

        var buffer = new MemoryBuffer(format.Width, format.Height, sixteen ? DrmFormat.Abgr16161616f : format.Format);
        try
        {
            if (!capture.Capture(source, new Box(0, 0, format.Width, format.Height), buffer))
            {
                reply.Error(IpcErrorCodes.Failed, "The capture did not complete.");
                return;
            }

            var size = OutputSize(buffer.Width, buffer.Height, scale, maxDimension ?? 0);
            PngColorChunks? chunks = null;
            if (sixteen)
            {
                chunks = capture.TryColorOf(source, out var color)
                    ? PngColorChunks.From(color.Description, color.Hdr)
                    : PngColorChunks.Srgb;
            }

            switch (to.Kind)
            {
                case IpcCaptureKind.Fd:
                    WriteFd(reply, buffer);
                    break;
                case IpcCaptureKind.Path:
                    WritePath(reply, buffer, to.Path!, size, sixteen, chunks);
                    break;
                default:
                    WriteInline(reply, buffer, size, sixteen, chunks);
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
            reply.Error(IpcErrorCodes.Failed, "The capture buffer has no CPU mapping.");
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
            reply.Error(IpcErrorCodes.Failed, $"The compositor could not make a memfd (errno {UnixSocket.LastError}).");
            return;
        }

        reply.Write(
            new IpcCaptureResult(buffer.Width, buffer.Height, Fd: reply.AttachFd(fd), Format: view.Format.ToString().ToLowerInvariant(), Stride: view.Stride),
            IpcJsonContext.Default.IpcCaptureResult);
    }

    private static (byte[] Png, int Width, int Height) Encode(MemoryBuffer buffer, (int Width, int Height) size, bool sixteen, PngColorChunks? chunks)
    {
        if (sixteen && BufferCapture.TryReadRgb16(buffer, out var rgb))
        {
            var (samples, wideWidth, wideHeight) = Scaled16(rgb, buffer.Width, buffer.Height, size);
            return (PngCodec.Encode16(samples, wideWidth, wideHeight, chunks), wideWidth, wideHeight);
        }

        var (rgba, width, height) = Scaled(BufferCapture.ReadRgba(buffer), buffer.Width, buffer.Height, size);
        return (PngCodec.Encode(rgba, width, height), width, height);
    }

    private static void WritePath(
        IpcReply reply, MemoryBuffer buffer, string path, (int Width, int Height) size, bool sixteen, PngColorChunks? chunks)
    {
        var (png, width, height) = Encode(buffer, size, sixteen, chunks);
        try
        {
            File.WriteAllBytes(path, png);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            reply.Error(IpcErrorCodes.Failed, $"The PNG cannot be written to '{path}'. {exception.Message}");
            return;
        }

        reply.Write(new IpcCaptureResult(width, height, Path: path), IpcJsonContext.Default.IpcCaptureResult);
    }

    private static void WriteInline(
        IpcReply reply, MemoryBuffer buffer, (int Width, int Height) size, bool sixteen, PngColorChunks? chunks)
    {
        var (png, width, height) = Encode(buffer, size, sixteen, chunks);
        if (((png.Length + 2) / 3 * 4) + InlineHeadroom > IpcProtocol.MaxMessageBytes)
        {
            reply.Error(IpcErrorCodes.TooLarge, $"The {png.Length}-byte PNG is too large for one reply. Ask for a path or an fd instead.");
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

    private static (ushort[] Rgb, int Width, int Height) Scaled16(ushort[] rgb, int width, int height, (int Width, int Height) size)
    {
        if (size.Width >= width && size.Height >= height)
        {
            return (rgb, width, height);
        }

        var (outWidth, outHeight) = size;
        var result = new ushort[outWidth * outHeight * 3];
        for (var y = 0; y < outHeight; y++)
        {
            var y0 = y * height / outHeight;
            var y1 = Math.Max(y0 + 1, (y + 1) * height / outHeight);
            for (var x = 0; x < outWidth; x++)
            {
                var x0 = x * width / outWidth;
                var x1 = Math.Max(x0 + 1, (x + 1) * width / outWidth);
                long r = 0, g = 0, b = 0;
                for (var sy = y0; sy < y1; sy++)
                {
                    for (var sx = x0; sx < x1; sx++)
                    {
                        var i = ((sy * width) + sx) * 3;
                        r += rgb[i];
                        g += rgb[i + 1];
                        b += rgb[i + 2];
                    }
                }

                var count = (y1 - y0) * (x1 - x0);
                var o = ((y * outWidth) + x) * 3;
                result[o] = (ushort)(r / count);
                result[o + 1] = (ushort)(g / count);
                result[o + 2] = (ushort)(b / count);
            }
        }

        return (result, outWidth, outHeight);
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
