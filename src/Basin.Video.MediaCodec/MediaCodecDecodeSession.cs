using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Diagnostics;
using static Basin.Video.MediaCodec.MediaCodecLog;
using static Basin.Video.MediaCodec.MediaCodecNative;

namespace Basin.Video.MediaCodec;

internal sealed unsafe class MediaCodecDecodeSession : IVideoDecodeSession
{
    private const long InputTimeoutUs = 100_000;
    private const long OutputTimeoutUs = 50_000;
    private const int OutputAttempts = 6;

    private readonly int _width;
    private readonly int _height;
    private readonly DrmFormat _format;
    private readonly string _mime;
    private nint _codec;
    private byte* _bgra;
    private long _bgraStride;
    private int _colorFormat;
    private int _stride;
    private int _sliceHeight;
    private int _frameWidth;
    private int _frameHeight;
    private int _cropLeft;
    private int _cropTop;
    private bool _formatKnown;
    private long _frameIndex;
    private bool _steady;
    private bool _disposed;

    internal MediaCodecDecodeSession(string mime, int width, int height, DrmFormat format)
    {
        if (!PixelRows.IsSupported(format))
        {
            throw new NotSupportedException($"MediaCodec frames are not packed into {format}");
        }

        _mime = mime;
        _width = width;
        _height = height;
        _format = format;
        _codec = CreateDecoder(mime);
        if (_codec == 0)
        {
            throw new NotSupportedException($"this device lists no {mime} decoder");
        }

        var configuration = AMediaFormat_new();
        SetString(configuration, "mime", mime);
        SetInt32(configuration, "width", width);
        SetInt32(configuration, "height", height);
        SetInt32(configuration, "low-latency", 1);
        var configured = AMediaCodec_configure(_codec, configuration, 0, 0, 0);
        AMediaFormat_delete(configuration);
        if (configured != 0)
        {
            AMediaCodec_delete(_codec);
            _codec = 0;
            throw new InvalidOperationException($"AMediaCodec_configure failed for {mime}: {configured}");
        }

        var started = AMediaCodec_start(_codec);
        if (started != 0)
        {
            AMediaCodec_delete(_codec);
            _codec = 0;
            throw new InvalidOperationException($"AMediaCodec_start failed for {mime}: {started}");
        }

        MediaCodecCensus.Track();
    }

    public bool Decode(ReadOnlySpan<byte> packet, nint destination, int stride)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var steady = _steady;
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
        var inputIndex = AMediaCodec_dequeueInputBuffer(_codec, InputTimeoutUs);
        if (inputIndex < 0)
        {
            Log.Warn($"the {_mime} decoder offered no input buffer within {InputTimeoutUs / 1000} ms");
            return false;
        }

        nuint capacity = 0;
        var input = AMediaCodec_getInputBuffer(_codec, (nuint)inputIndex, &capacity);
        if (input == null || capacity < (nuint)packet.Length)
        {
            Log.Warn($"a {packet.Length} byte packet does not fit the decoder's {capacity} byte input buffer");
            AMediaCodec_queueInputBuffer(_codec, (nuint)inputIndex, 0, 0, 0, 0);
            return false;
        }

        packet.CopyTo(new Span<byte>(input, packet.Length));
        var queued = AMediaCodec_queueInputBuffer(
            _codec, (nuint)inputIndex, 0, (nuint)packet.Length, (ulong)(_frameIndex * 16_667), 0);
        _frameIndex++;
        if (queued != 0)
        {
            Log.Warn($"AMediaCodec_queueInputBuffer failed: {queued}");
            return false;
        }

        AMediaCodecBufferInfo info;
        for (var attempt = 0; attempt < OutputAttempts; attempt++)
        {
            var outputIndex = AMediaCodec_dequeueOutputBuffer(_codec, &info, OutputTimeoutUs);
            if (outputIndex >= 0)
            {
                var produced = false;
                try
                {
                    produced = Land((nuint)outputIndex, info, destination, stride);
                }
                finally
                {
                    AMediaCodec_releaseOutputBuffer(_codec, (nuint)outputIndex, 0);
                }

                if (produced)
                {
                    _steady = true;
                }

                return produced;
            }

            if (outputIndex == InfoOutputFormatChanged)
            {
                _formatKnown = false;
                continue;
            }

            if (outputIndex == InfoOutputBuffersChanged || outputIndex == InfoTryAgainLater)
            {
                continue;
            }

            Log.Warn($"AMediaCodec_dequeueOutputBuffer failed: {outputIndex}");
            return false;
        }

        Log.Warn($"the {_mime} decoder produced no frame within {OutputAttempts * OutputTimeoutUs / 1000} ms, and the peer encodes with delay 0");
        return false;
    }

    private bool Land(nuint index, AMediaCodecBufferInfo info, nint destination, int stride)
    {
        if (!_formatKnown && !ReadOutputFormat())
        {
            return false;
        }

        nuint size = 0;
        var output = AMediaCodec_getOutputBuffer(_codec, index, &size);
        if (output == null || info.Size <= 0)
        {
            return false;
        }

        var lumaSize = (long)_stride * _sliceHeight;
        var chromaStride = _stride;
        var chromaRows = (_sliceHeight + 1) / 2;
        var needed = lumaSize + ((long)chromaStride * chromaRows);
        if (info.Offset + needed > (long)size)
        {
            Log.Warn($"a {size} byte output buffer is smaller than the {needed} bytes its format describes");
            return false;
        }

        var visibleWidth = _frameWidth;
        var visibleHeight = _frameHeight;
        if (visibleWidth < _width || visibleHeight < _height)
        {
            Log.Warn($"a {visibleWidth}x{visibleHeight} frame cannot fill a {_width}x{_height} buffer");
            return false;
        }

        var y = output + info.Offset + ((long)_cropTop * _stride) + _cropLeft;
        var chroma = output + info.Offset + lumaSize;
        switch (_colorFormat)
        {
            case ColorFormatYuv420Planar:
            case ColorFormatYuv420PackedPlanar:
                var chromaPlaneStride = (_stride + 1) / 2;
                var chromaPlaneSize = (long)chromaPlaneStride * chromaRows;
                var u = chroma + ((_cropTop / 2) * chromaPlaneStride) + (_cropLeft / 2);
                var v = chroma + chromaPlaneSize + ((_cropTop / 2) * chromaPlaneStride) + (_cropLeft / 2);
                Yuv420ToBgra.ConvertPlanar(y, _stride, u, v, chromaPlaneStride, _bgra, _bgraStride, _width, _height);
                break;

            default:
                var uv = chroma + ((_cropTop / 2) * _stride) + (_cropLeft & ~1);
                Yuv420ToBgra.ConvertSemiPlanar(y, _stride, uv, _stride, crFirst: false, _bgra, _bgraStride, _width, _height);
                break;
        }

        PixelRows.Copy(_bgra, _bgraStride, destination, stride, _width, _height, _format);
        return true;
    }

    private bool ReadOutputFormat()
    {
        var format = AMediaCodec_getOutputFormat(_codec);
        if (format == 0)
        {
            Log.Warn($"the {_mime} decoder reports no output format");
            return false;
        }

        try
        {
            if (!TryGetInt32(format, "color-format\0"u8, out _colorFormat)
                || !TryGetInt32(format, "width\0"u8, out _frameWidth)
                || !TryGetInt32(format, "height\0"u8, out _frameHeight))
            {
                Log.Warn($"the {_mime} decoder's output format names no color format or size");
                return false;
            }

            if (!TryGetInt32(format, "stride\0"u8, out _stride) || _stride <= 0)
            {
                _stride = _frameWidth;
            }

            if (!TryGetInt32(format, "slice-height\0"u8, out _sliceHeight) || _sliceHeight <= 0)
            {
                _sliceHeight = _frameHeight;
            }

            if (TryGetInt32(format, "crop-left\0"u8, out var left) && TryGetInt32(format, "crop-right\0"u8, out var right)
                && TryGetInt32(format, "crop-top\0"u8, out var top) && TryGetInt32(format, "crop-bottom\0"u8, out var bottom))
            {
                _cropLeft = left;
                _cropTop = top;
                _frameWidth = right - left + 1;
                _frameHeight = bottom - top + 1;
            }
            else
            {
                _cropLeft = 0;
                _cropTop = 0;
            }
        }
        finally
        {
            AMediaFormat_delete(format);
        }

        switch (_colorFormat)
        {
            case ColorFormatYuv420Planar:
            case ColorFormatYuv420PackedPlanar:
            case ColorFormatYuv420SemiPlanar:
            case ColorFormatYuv420PackedSemiPlanar:
            case ColorQcomFormatYuv420SemiPlanar:
            case ColorTiFormatYuv420PackedSemiPlanar:
                break;

            case ColorFormatYuv420Flexible:
                Log.Warn($"the {_mime} decoder reports the flexible YUV420 format; its buffer is read as semi-planar");
                break;

            default:
                Log.Warn($"the {_mime} decoder produces color format 0x{_colorFormat:x8}, which this session cannot read");
                return false;
        }

        var bgraStride = (long)_width * 4;
        if (_bgra == null || bgraStride != _bgraStride)
        {
            NativeMemory.Free(_bgra);
            _bgra = (byte*)NativeMemory.AllocZeroed((nuint)(bgraStride * _height));
            _bgraStride = bgraStride;
        }

        _formatKnown = true;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_codec != 0)
        {
            AMediaCodec_stop(_codec);
            AMediaCodec_delete(_codec);
            _codec = 0;
            MediaCodecCensus.Untrack();
        }

        NativeMemory.Free(_bgra);
        _bgra = null;
    }
}
