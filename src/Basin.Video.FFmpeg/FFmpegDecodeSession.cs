using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin.Video.FFmpeg;

internal sealed unsafe class FFmpegDecodeSession : IVideoDecodeSession
{
    private const int ColorspaceItu601 = 5;
    private const int ScaleBilinear = 2;

    private readonly int _width;
    private readonly int _height;
    private readonly int _bytesPerPixel;
    private readonly int _destinationFormat;
    private nint _context;
    private nint _packet;
    private nint _frame;
    private nint _transferFrame;
    private readonly int _hardwareFormat = int.MinValue;
    private readonly string _codecName;
    private bool _reportedPath;
    private nint _scaler;
    private nint _converted;
    private int _convertedWidth;
    private int _convertedHeight;
    private int _sourceFormat = -1;
    private bool _disposed;

    internal bool DecodedOnHardware { get; private set; }

    internal FFmpegDecodeSession(string codecName, int width, int height, DrmFormat format, FFmpegHardware? hardware = null)
    {
        _codecName = codecName;
        _width = width;
        _height = height;
        _bytesPerPixel = format.BytesPerPixel();
        _destinationFormat = DestinationFormat(format);
        if (_destinationFormat < 0)
        {
            throw new NotSupportedException($"swscale names no destination for {format}");
        }

        var id = FFmpegNative.CodecId(codecName);
        var codec = id < 0 ? 0 : FFmpegNative.avcodec_find_decoder(id);
        if (codec == 0)
        {
            throw new NotSupportedException($"the system FFmpeg has no {codecName} decoder");
        }

        _context = FFmpegNative.avcodec_alloc_context3(codec);
        if (_context == 0)
        {
            throw new InvalidOperationException("avcodec_alloc_context3 returned null");
        }

        if (hardware is not null)
        {
            var device = FFmpegNative.av_buffer_ref(hardware.Device);
            if (device == 0)
            {
                Basin.Diagnostics.BasinLog.Warn($"ffmpeg: the {hardware.Name} device could not be referenced; this stream decodes in software");
            }
            else
            {
                var layoutForContext = FFmpegNative.Layout;
                *(nint*)(_context + layoutForContext.ContextGetFormat) = FFmpegNative.ChooseFormatPointer;
                *(nint*)(_context + layoutForContext.ContextHwDeviceCtx) = device;
                _hardwareFormat = hardware.PixelFormat;
                _transferFrame = FFmpegNative.av_frame_alloc();
                FFmpegCensus.Track();
            }
        }

        nint options = 0;
        Span<byte> threads = [(byte)'t', (byte)'h', (byte)'r', (byte)'e', (byte)'a', (byte)'d', (byte)'s', 0];
        Span<byte> one = [(byte)'1', 0];
        fixed (byte* key = threads)
        fixed (byte* value = one)
        {
            _ = FFmpegNative.av_dict_set(&options, key, value, 0);
        }

        var opened = FFmpegNative.avcodec_open2(_context, codec, &options);
        FFmpegNative.av_dict_free(&options);
        if (opened < 0)
        {
            var context = _context;
            FFmpegNative.avcodec_free_context(&context);
            _context = 0;
            throw new InvalidOperationException(
                $"avcodec_open2 failed for {codecName}: {FFmpegNative.DescribeError(opened)}");
        }

        _packet = FFmpegNative.av_packet_alloc();
        _frame = FFmpegNative.av_frame_alloc();
        FFmpegCensus.Track();
        FFmpegCensus.Track();
        FFmpegCensus.Track();
    }

    public bool Decode(ReadOnlySpan<byte> packet, nint destination, int stride)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var steady = _scaler != 0;
        if (steady)
        {
            AllocationScope.Begin();
        }

        try
        {
            return DecodeCore(packet, destination, stride);
        }
        finally
        {
            if (steady)
            {
                AllocationScope.End();
            }
        }
    }

    private bool DecodeCore(ReadOnlySpan<byte> packet, nint destination, int stride)
    {
        var layout = FFmpegNative.Layout;
        int sent;
        fixed (byte* data = packet)
        {
            *(nint*)(_packet + layout.PacketData) = (nint)data;
            *(int*)(_packet + layout.PacketSize) = packet.Length;
            sent = FFmpegNative.avcodec_send_packet(_context, _packet);
            *(nint*)(_packet + layout.PacketData) = 0;
            *(int*)(_packet + layout.PacketSize) = 0;
        }

        if (sent < 0)
        {
            BasinLog.Warn($"ffmpeg: a video packet was refused: {FFmpegNative.DescribeError(sent)}");
            return false;
        }

        var received = FFmpegNative.avcodec_receive_frame(_context, _frame);
        if (received < 0)
        {
            BasinLog.Warn(
                $"ffmpeg: a packet produced no frame ({FFmpegNative.DescribeError(received)}), and the peer encodes with delay 0, so this is a fault rather than buffering");
            return false;
        }

        var frame = _frame;
        if (_transferFrame != 0 && *(int*)(_frame + layout.FrameFormat) == _hardwareFormat)
        {
            FFmpegNative.av_frame_unref(_transferFrame);
            var moved = FFmpegNative.av_hwframe_transfer_data(_transferFrame, _frame, 0);
            FFmpegNative.av_frame_unref(_frame);
            if (moved < 0)
            {
                BasinLog.Warn($"ffmpeg: a hardware frame did not transfer to system memory: {FFmpegNative.DescribeError(moved)}");
                return false;
            }

            frame = _transferFrame;
            if (!_reportedPath)
            {
                _reportedPath = true;
                DecodedOnHardware = true;
                BasinLog.Info($"ffmpeg: {_codecName} decodes on hardware");
            }
        }
        else if (!_reportedPath)
        {
            _reportedPath = true;
            if (_transferFrame != 0)
            {
                BasinLog.Info($"ffmpeg: {_codecName} decodes in software; the device declined the stream");
            }
        }

        var frameWidth = *(int*)(frame + layout.FrameWidth);
        var frameHeight = *(int*)(frame + layout.FrameHeight);
        var sourceFormat = *(int*)(frame + layout.FrameFormat);
        if (frameWidth < _width || frameHeight < _height)
        {
            BasinLog.Warn($"ffmpeg: a {frameWidth}x{frameHeight} frame cannot fill a {_width}x{_height} buffer");
            FFmpegNative.av_frame_unref(frame);
            return false;
        }

        if (_scaler == 0 || sourceFormat != _sourceFormat
            || frameWidth != _convertedWidth || frameHeight != _convertedHeight)
        {
            var scaler = FFmpegNative.sws_getCachedContext(
                _scaler, frameWidth, frameHeight, sourceFormat, frameWidth, frameHeight, _destinationFormat,
                ScaleBilinear, 0, 0, null);
            if (scaler == 0)
            {
                BasinLog.Warn($"ffmpeg: swscale cannot convert format {sourceFormat} to {_destinationFormat}");
                FFmpegNative.av_frame_unref(frame);
                return false;
            }

            if (_scaler == 0)
            {
                FFmpegCensus.Track();
            }

            _scaler = scaler;
            _sourceFormat = sourceFormat;
            var coefficients = FFmpegNative.sws_getCoefficients(ColorspaceItu601);
            _ = FFmpegNative.sws_setColorspaceDetails(
                _scaler, coefficients, 0, coefficients, 1, 0, 1 << 16, 1 << 16);

            NativeMemory.Free((void*)_converted);
            _converted = (nint)NativeMemory.AllocZeroed(
                (nuint)(((long)frameWidth * _bytesPerPixel * frameHeight) + 64));
            _convertedWidth = frameWidth;
            _convertedHeight = frameHeight;
        }

        var sourceData = (byte**)(frame + layout.FrameData);
        var sourceLines = (int*)(frame + layout.FrameLinesize);
        var convertedStride = _convertedWidth * _bytesPerPixel;
        var destinationData = stackalloc byte*[4];
        var destinationLines = stackalloc int[4];
        destinationData[0] = (byte*)_converted;
        destinationLines[0] = convertedStride;
        var scaled = FFmpegNative.sws_scale(
            _scaler, sourceData, sourceLines, 0, _convertedHeight, destinationData, destinationLines);
        FFmpegNative.av_frame_unref(frame);
        if (scaled != _convertedHeight)
        {
            BasinLog.Warn($"ffmpeg: swscale wrote {scaled} rows of {_convertedHeight}");
            return false;
        }

        var rowBytes = _width * _bytesPerPixel;
        for (var row = 0; row < _height; row++)
        {
            NativeMemory.Copy(
                (void*)(_converted + ((long)row * convertedStride)),
                (void*)(destination + ((long)row * stride)),
                (nuint)rowBytes);
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        NativeMemory.Free((void*)_converted);
        _converted = 0;
        if (_scaler != 0)
        {
            FFmpegNative.sws_freeContext(_scaler);
            _scaler = 0;
            FFmpegCensus.Untrack();
        }

        if (_transferFrame != 0)
        {
            var transferFrame = _transferFrame;
            FFmpegNative.av_frame_free(&transferFrame);
            _transferFrame = 0;
            FFmpegCensus.Untrack();
        }

        var frame = _frame;
        FFmpegNative.av_frame_free(&frame);
        _frame = 0;
        var packet = _packet;
        FFmpegNative.av_packet_free(&packet);
        _packet = 0;
        var context = _context;
        FFmpegNative.avcodec_free_context(&context);
        _context = 0;
        FFmpegCensus.Untrack();
        FFmpegCensus.Untrack();
        FFmpegCensus.Untrack();
    }

    private static int DestinationFormat(DrmFormat format)
    {
        var name = format switch
        {
            DrmFormat.Xrgb8888 or DrmFormat.Argb8888 => "bgra",
            DrmFormat.Xbgr8888 or DrmFormat.Abgr8888 => "rgba",
            DrmFormat.Xrgb2101010 => "x2rgb10le",
            DrmFormat.Xbgr2101010 => "x2bgr10le",
            DrmFormat.Rgb565 => "rgb565le",
            _ => null,
        };
        return name is null ? -1 : FFmpegNative.PixelFormat(name);
    }
}
