using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Diagnostics;
using static Basin.Video.VideoToolbox.VideoToolboxLog;
using static Basin.Video.VideoToolbox.VideoToolboxNative;

namespace Basin.Video.VideoToolbox;

internal sealed unsafe class VideoToolboxDecodeSession : IVideoDecodeSession
{
    private const int Timescale = 90_000;

    private readonly VideoCodec _codec;
    private readonly int _width;
    private readonly int _height;
    private readonly DrmFormat _format;
    private readonly GCHandle _self;
    private byte* _payload;
    private nuint _payloadCapacity;
    private nuint _payloadLength;
    private byte* _sps;
    private int _spsLength;
    private byte* _pps;
    private int _ppsLength;
    private bool _parameterSetsChanged;
    private nint _formatDescription;
    private nint _session;
    private nint _destination;
    private int _stride;
    private bool _produced;
    private int _callbackStatus;
    private long _frameIndex;
    private bool _steady;
    private bool _disposed;
    private long _lastArrival;
    private long _intervalTicks;
    private long _intervalMax;
    private long _decodeTicks;
    private long _decodeMax;
    private long _copyTicks;
    private long _copyMax;
    private int _timed;

    internal VideoToolboxDecodeSession(VideoCodec codec, int width, int height, DrmFormat format)
    {
        if (!PixelRows.IsSupported(format))
        {
            throw new NotSupportedException($"VideoToolbox frames are not packed into {format}");
        }

        _codec = codec;
        _width = width;
        _height = height;
        _format = format;
        _self = GCHandle.Alloc(this, GCHandleType.Normal);
        VideoToolboxCensus.Track();
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
        var arrived = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_lastArrival != 0)
        {
            var interval = arrived - _lastArrival;
            _intervalTicks += interval;
            _intervalMax = Math.Max(_intervalMax, interval);
        }

        _lastArrival = arrived;
        _payloadLength = 0;
        switch (_codec)
        {
            case VideoCodec.H264:
                if (!GatherH264(packet))
                {
                    return false;
                }

                break;

            case VideoCodec.Vp9:
                if (_formatDescription == 0 && !DescribeVp9(packet))
                {
                    return false;
                }

                Append(packet);
                break;

            default:
                if (_formatDescription == 0 && !DescribeAv1(packet))
                {
                    return false;
                }

                Append(packet);
                break;
        }

        if (_payloadLength == 0)
        {
            Log.Warn($"a video packet carried no picture data");
            return false;
        }

        if (!EnsureSession())
        {
            return false;
        }

        nint block = 0;
        var status = CMBlockBufferCreateWithMemoryBlock(
            0, _payload, _payloadCapacity, AllocatorNull, 0, 0, _payloadLength, 0, &block);
        if (status != 0 || block == 0)
        {
            Log.Warn($"CMBlockBufferCreateWithMemoryBlock failed: {status}");
            return false;
        }

        var timing = new CMSampleTimingInfo
        {
            Duration = new CMTime { Value = Timescale / 60, Timescale = Timescale, Flags = TimeFlagsValid },
            PresentationTimeStamp = new CMTime { Value = _frameIndex * (Timescale / 60), Timescale = Timescale, Flags = TimeFlagsValid },
            DecodeTimeStamp = new CMTime { Value = _frameIndex * (Timescale / 60), Timescale = Timescale, Flags = TimeFlagsValid },
        };
        _frameIndex++;
        var size = _payloadLength;
        nint sample = 0;
        status = CMSampleBufferCreateReady(0, block, _formatDescription, 1, 1, &timing, 1, &size, &sample);
        CFRelease(block);
        if (status != 0 || sample == 0)
        {
            Log.Warn($"CMSampleBufferCreateReady failed: {status}");
            return false;
        }

        _destination = destination;
        _stride = stride;
        _produced = false;
        _callbackStatus = 0;
        uint info = 0;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        status = VTDecompressionSessionDecodeFrame(_session, sample, 0, 0, &info);
        var waited = VTDecompressionSessionWaitForAsynchronousFrames(_session);
        var decoded = System.Diagnostics.Stopwatch.GetTimestamp() - started;
        _decodeTicks += decoded;
        _decodeMax = Math.Max(_decodeMax, decoded);
        CFRelease(sample);
        _destination = 0;
        if (++_timed == 120)
        {
            var frequency = (double)System.Diagnostics.Stopwatch.Frequency / 1000;
            Log.Debug(
                $"{_width}x{_height} {_format} (simd {System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated}): {_timed} frames, packet interval {_intervalTicks / frequency / (_timed - 1):0.0} ms mean {_intervalMax / frequency:0.0} ms max, " +
                $"decode {_decodeTicks / frequency / _timed:0.00} ms mean {_decodeMax / frequency:0.00} ms max (copy {_copyTicks / frequency / _timed:0.00} ms mean {_copyMax / frequency:0.00} ms max)");
            _timed = 0;
            _intervalTicks = 0;
            _intervalMax = 0;
            _decodeTicks = 0;
            _decodeMax = 0;
            _copyTicks = 0;
            _copyMax = 0;
        }

        if (status != 0)
        {
            Log.Warn($"VTDecompressionSessionDecodeFrame refused a frame: {status}");
            return false;
        }

        if (waited != 0)
        {
            Log.Warn($"VTDecompressionSessionWaitForAsynchronousFrames failed: {waited}");
        }

        if (!_produced)
        {
            if (_callbackStatus != 0)
            {
                Log.Warn($"VideoToolbox produced no frame: {_callbackStatus}");
            }
            else
            {
                Log.Warn($"VideoToolbox produced no frame for a packet, and the peer encodes with delay 0");
            }

            return false;
        }

        _steady = true;
        return true;
    }

    private bool GatherH264(ReadOnlySpan<byte> packet)
    {
        var offset = 0;
        while (H264AnnexB.TryNext(packet, ref offset, out var start, out var length))
        {
            var nal = packet.Slice(start, length);
            switch (H264AnnexB.TypeOf(nal))
            {
                case H264AnnexB.NalSps:
                    if (!nal.SequenceEqual(new ReadOnlySpan<byte>(_sps, _spsLength)))
                    {
                        _sps = Keep(_sps, nal);
                        _spsLength = nal.Length;
                        _parameterSetsChanged = true;
                    }

                    break;

                case H264AnnexB.NalPps:
                    if (!nal.SequenceEqual(new ReadOnlySpan<byte>(_pps, _ppsLength)))
                    {
                        _pps = Keep(_pps, nal);
                        _ppsLength = nal.Length;
                        _parameterSetsChanged = true;
                    }

                    break;

                case H264AnnexB.NalAccessUnitDelimiter:
                    break;

                default:
                    Reserve(_payloadLength + 4 + (nuint)length);
                    var lengthPrefix = _payload + _payloadLength;
                    lengthPrefix[0] = (byte)(length >> 24);
                    lengthPrefix[1] = (byte)(length >> 16);
                    lengthPrefix[2] = (byte)(length >> 8);
                    lengthPrefix[3] = (byte)length;
                    nal.CopyTo(new Span<byte>(lengthPrefix + 4, length));
                    _payloadLength += 4 + (nuint)length;
                    break;
            }
        }

        if (_formatDescription == 0 || _parameterSetsChanged)
        {
            if (_spsLength < 4 || _ppsLength == 0)
            {
                Log.Warn($"an H.264 packet arrived before any SPS and PPS");
                return false;
            }

            var recordLength = 11 + _spsLength + _ppsLength;
            var record = (byte*)NativeMemory.Alloc((nuint)recordLength);
            try
            {
                record[0] = 1;
                record[1] = _sps[1];
                record[2] = _sps[2];
                record[3] = _sps[3];
                record[4] = 0xff;
                record[5] = 0xe1;
                record[6] = (byte)(_spsLength >> 8);
                record[7] = (byte)_spsLength;
                Buffer.MemoryCopy(_sps, record + 8, _spsLength, _spsLength);
                var pps = record + 8 + _spsLength;
                pps[0] = 1;
                pps[1] = (byte)(_ppsLength >> 8);
                pps[2] = (byte)_ppsLength;
                Buffer.MemoryCopy(_pps, pps + 3, _ppsLength, _ppsLength);
                if (!DescribeWithAtom(CodecTypeH264, "avcC", new ReadOnlySpan<byte>(record, recordLength)))
                {
                    return false;
                }
            }
            finally
            {
                NativeMemory.Free(record);
            }

            _parameterSetsChanged = false;
        }

        return true;
    }

    private static byte* Keep(byte* existing, ReadOnlySpan<byte> bytes)
    {
        var kept = (byte*)NativeMemory.Realloc(existing, (nuint)Math.Max(bytes.Length, 1));
        bytes.CopyTo(new Span<byte>(kept, bytes.Length));
        return kept;
    }

    private bool DescribeVp9(ReadOnlySpan<byte> frame)
    {
        Span<byte> record = stackalloc byte[12];
        if (!Vp9CodecConfiguration.TryBuild(frame, record, out var written))
        {
            Log.Warn($"a VP9 stream did not open on a key frame this session can describe");
            return false;
        }

        return DescribeWithAtom(CodecTypeVp9, "vpcC", record[..written]);
    }

    private bool DescribeAv1(ReadOnlySpan<byte> temporalUnit)
    {
        var record = new byte[4 + temporalUnit.Length];
        if (!Av1CodecConfiguration.TryBuild(temporalUnit, record, out var written))
        {
            Log.Warn($"an AV1 stream did not open on a temporal unit carrying its sequence header");
            return false;
        }

        return DescribeWithAtom(CodecTypeAv1, "av1C", record.AsSpan(0, written));
    }

    private bool DescribeWithAtom(uint codecType, string atom, ReadOnlySpan<byte> record)
    {
        var atomKey = CreateString(atom);
        var atomData = CreateData(record);
        var atoms = CreateDictionary([atomKey], [atomData]);
        Span<nint> keys =
        [
            CreateString("SampleDescriptionExtensionAtoms"),
            CreateString("CVImageBufferYCbCrMatrix"),
            CreateString("CVImageBufferColorPrimaries"),
            CreateString("CVImageBufferTransferFunction"),
            CreateString("FullRangeVideo"),
        ];
        Span<nint> values =
        [
            atoms,
            CreateString("ITU_R_601_4"),
            CreateString("SMPTE_C"),
            CreateString("ITU_R_709_2"),
            BooleanFalse,
        ];
        var extensions = CreateDictionary(keys, values);
        nint description = 0;
        var status = CMVideoFormatDescriptionCreate(0, codecType, _width, _height, extensions, &description);
        CFRelease(extensions);
        foreach (var key in keys)
        {
            CFRelease(key);
        }

        CFRelease(values[1]);
        CFRelease(values[2]);
        CFRelease(values[3]);
        CFRelease(atoms);
        CFRelease(atomData);
        CFRelease(atomKey);
        if (status != 0 || description == 0)
        {
            Log.Warn($"CMVideoFormatDescriptionCreate failed for {atom}: {status}");
            return false;
        }

        ReplaceFormatDescription(description);
        return true;
    }

    private void ReplaceFormatDescription(nint description)
    {
        if (_formatDescription != 0)
        {
            CFRelease(_formatDescription);
        }

        _formatDescription = description;
    }

    private bool EnsureSession()
    {
        if (_session != 0)
        {
            if (VTDecompressionSessionCanAcceptFormatDescription(_session, _formatDescription) != 0)
            {
                return true;
            }

            VTDecompressionSessionInvalidate(_session);
            CFRelease(_session);
            _session = 0;
            VideoToolboxCensus.Untrack();
        }

        var formatKey = CreateString("PixelFormatType");
        var formatValue = CreateNumber((int)PixelFormat32Bgra);
        var attributes = CreateDictionary([formatKey], [formatValue]);
        var record = new VTDecompressionOutputCallbackRecord
        {
            Callback = (nint)(delegate* unmanaged<nint, nint, int, uint, nint, CMTime, CMTime, void>)&OnFrame,
            RefCon = GCHandle.ToIntPtr(_self),
        };
        nint session = 0;
        var status = VTDecompressionSessionCreate(0, _formatDescription, 0, attributes, &record, &session);
        CFRelease(attributes);
        CFRelease(formatValue);
        CFRelease(formatKey);
        if (status != 0 || session == 0)
        {
            Log.Warn($"VTDecompressionSessionCreate failed: {status}");
            return false;
        }

        _session = session;
        VideoToolboxCensus.Track();
        return VTDecompressionSessionCanAcceptFormatDescription(_session, _formatDescription) != 0;
    }

    private void Append(ReadOnlySpan<byte> bytes)
    {
        Reserve(_payloadLength + (nuint)bytes.Length);
        bytes.CopyTo(new Span<byte>(_payload + _payloadLength, bytes.Length));
        _payloadLength += (nuint)bytes.Length;
    }

    private void Reserve(nuint needed)
    {
        if (needed <= _payloadCapacity)
        {
            return;
        }

        var capacity = Math.Max(needed, Math.Max(_payloadCapacity * 2, 64 * 1024));
        _payload = (byte*)NativeMemory.Realloc(_payload, capacity);
        _payloadCapacity = capacity;
    }

    [UnmanagedCallersOnly]
    private static void OnFrame(
        nint refCon, nint sourceFrameRefCon, int status, uint infoFlags, nint imageBuffer, CMTime timestamp, CMTime duration)
    {
        try
        {
            if (GCHandle.FromIntPtr(refCon).Target is not VideoToolboxDecodeSession session)
            {
                return;
            }

            session.Land(status, imageBuffer);
        }
        catch (Exception)
        {
        }
    }

    private void Land(int status, nint imageBuffer)
    {
        if (status != 0 || imageBuffer == 0)
        {
            _callbackStatus = status;
            return;
        }

        if (_destination == 0)
        {
            return;
        }

        var pixelFormat = CVPixelBufferGetPixelFormatType(imageBuffer);
        if (pixelFormat != PixelFormat32Bgra)
        {
            _callbackStatus = -1;
            Log.Warn($"VideoToolbox produced pixel format 0x{pixelFormat:x8} rather than BGRA");
            return;
        }

        var frameWidth = (long)CVPixelBufferGetWidth(imageBuffer);
        var frameHeight = (long)CVPixelBufferGetHeight(imageBuffer);
        if (frameWidth < _width || frameHeight < _height)
        {
            _callbackStatus = -1;
            Log.Warn($"a {frameWidth}x{frameHeight} frame cannot fill a {_width}x{_height} buffer");
            return;
        }

        if (CVPixelBufferLockBaseAddress(imageBuffer, PixelBufferLockReadOnly) != 0)
        {
            _callbackStatus = -1;
            return;
        }

        try
        {
            var source = (byte*)CVPixelBufferGetBaseAddress(imageBuffer);
            var sourceStride = (long)CVPixelBufferGetBytesPerRow(imageBuffer);
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            PixelRows.Copy(source, sourceStride, _destination, _stride, _width, _height, _format);
            var copied = System.Diagnostics.Stopwatch.GetTimestamp() - started;
            _copyTicks += copied;
            _copyMax = Math.Max(_copyMax, copied);
            _produced = true;
        }
        finally
        {
            CVPixelBufferUnlockBaseAddress(imageBuffer, PixelBufferLockReadOnly);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_session != 0)
        {
            VTDecompressionSessionInvalidate(_session);
            CFRelease(_session);
            _session = 0;
            VideoToolboxCensus.Untrack();
        }

        if (_formatDescription != 0)
        {
            CFRelease(_formatDescription);
            _formatDescription = 0;
        }

        NativeMemory.Free(_payload);
        _payload = null;
        _payloadCapacity = 0;
        NativeMemory.Free(_sps);
        _sps = null;
        NativeMemory.Free(_pps);
        _pps = null;
        _self.Free();
        VideoToolboxCensus.Untrack();
    }
}
