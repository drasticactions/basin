using System.Runtime.InteropServices;
using static Basin.Video.MediaCodec.MediaCodecLog;

namespace Basin.Video.MediaCodec;

internal static unsafe class MediaCodecNative
{
    internal const string Library = "libmediandk.so";

    internal const nint InfoOutputBuffersChanged = -3;
    internal const nint InfoOutputFormatChanged = -2;
    internal const nint InfoTryAgainLater = -1;

    internal const int ColorFormatYuv420Planar = 19;
    internal const int ColorFormatYuv420PackedPlanar = 20;
    internal const int ColorFormatYuv420SemiPlanar = 21;
    internal const int ColorFormatYuv420PackedSemiPlanar = 39;
    internal const int ColorFormatYuv420Flexible = 0x7f420888;
    internal const int ColorQcomFormatYuv420SemiPlanar = 0x7fa30c00;
    internal const int ColorTiFormatYuv420PackedSemiPlanar = 0x7f000100;

    private static readonly object Gate = new();
    private static bool _probed;
    private static string? _whyNot;

    internal static bool TryLoad(out string? whyNot)
    {
        lock (Gate)
        {
            if (_probed)
            {
                whyNot = _whyNot;
                return _whyNot is null;
            }

            _probed = true;
            _whyNot = Probe();
            whyNot = _whyNot;
            return _whyNot is null;
        }
    }

    private static string? Probe()
    {
        if (!NativeLibrary.TryLoad(Library, out var handle))
        {
            return "libmediandk.so does not load on this host";
        }

        if (!NativeLibrary.TryGetExport(handle, "AMediaCodec_createDecoderByType", out _))
        {
            return "the libmediandk.so on this host exports no AMediaCodec_createDecoderByType";
        }

        Log.Debug($"decode over libmediandk.so");
        return null;
    }

    internal static nint CreateDecoder(string mime)
    {
        Span<byte> bytes = stackalloc byte[64];
        var written = System.Text.Encoding.ASCII.GetBytes(mime, bytes[..^1]);
        bytes[written] = 0;
        fixed (byte* text = bytes)
        {
            return AMediaCodec_createDecoderByType(text);
        }
    }

    internal static void SetString(nint format, string key, string value)
    {
        Span<byte> keyBytes = stackalloc byte[64];
        Span<byte> valueBytes = stackalloc byte[64];
        var keyWritten = System.Text.Encoding.ASCII.GetBytes(key, keyBytes[..^1]);
        var valueWritten = System.Text.Encoding.ASCII.GetBytes(value, valueBytes[..^1]);
        keyBytes[keyWritten] = 0;
        valueBytes[valueWritten] = 0;
        fixed (byte* keyText = keyBytes)
        fixed (byte* valueText = valueBytes)
        {
            AMediaFormat_setString(format, keyText, valueText);
        }
    }

    internal static void SetInt32(nint format, string key, int value)
    {
        Span<byte> keyBytes = stackalloc byte[64];
        var keyWritten = System.Text.Encoding.ASCII.GetBytes(key, keyBytes[..^1]);
        keyBytes[keyWritten] = 0;
        fixed (byte* keyText = keyBytes)
        {
            AMediaFormat_setInt32(format, keyText, value);
        }
    }

    internal static bool TryGetInt32(nint format, ReadOnlySpan<byte> key, out int value)
    {
        fixed (byte* keyText = key)
        fixed (int* output = &value)
        {
            return AMediaFormat_getInt32(format, keyText, output) != 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AMediaCodecBufferInfo
    {
        public int Offset;

        public int Size;

        public long PresentationTimeUs;

        public uint Flags;
    }

    [DllImport(Library)]
    private static extern nint AMediaCodec_createDecoderByType(byte* mimeType);

    [DllImport(Library)]
    internal static extern int AMediaCodec_delete(nint codec);

    [DllImport(Library)]
    internal static extern int AMediaCodec_configure(nint codec, nint format, nint surface, nint crypto, uint flags);

    [DllImport(Library)]
    internal static extern int AMediaCodec_start(nint codec);

    [DllImport(Library)]
    internal static extern int AMediaCodec_stop(nint codec);

    [DllImport(Library)]
    internal static extern nint AMediaCodec_dequeueInputBuffer(nint codec, long timeoutUs);

    [DllImport(Library)]
    internal static extern byte* AMediaCodec_getInputBuffer(nint codec, nuint index, nuint* outSize);

    [DllImport(Library)]
    internal static extern int AMediaCodec_queueInputBuffer(
        nint codec, nuint index, nint offset, nuint size, ulong time, uint flags);

    [DllImport(Library)]
    internal static extern nint AMediaCodec_dequeueOutputBuffer(nint codec, AMediaCodecBufferInfo* info, long timeoutUs);

    [DllImport(Library)]
    internal static extern byte* AMediaCodec_getOutputBuffer(nint codec, nuint index, nuint* outSize);

    [DllImport(Library)]
    internal static extern nint AMediaCodec_getOutputFormat(nint codec);

    [DllImport(Library)]
    internal static extern int AMediaCodec_releaseOutputBuffer(nint codec, nuint index, byte render);

    [DllImport(Library)]
    internal static extern nint AMediaFormat_new();

    [DllImport(Library)]
    internal static extern int AMediaFormat_delete(nint format);

    [DllImport(Library)]
    private static extern void AMediaFormat_setString(nint format, byte* name, byte* value);

    [DllImport(Library)]
    private static extern void AMediaFormat_setInt32(nint format, byte* name, int value);

    [DllImport(Library)]
    private static extern byte AMediaFormat_getInt32(nint format, byte* name, int* output);
}
