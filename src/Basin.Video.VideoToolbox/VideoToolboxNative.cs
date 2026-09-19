using System.Runtime.InteropServices;
using static Basin.Video.VideoToolbox.VideoToolboxLog;

namespace Basin.Video.VideoToolbox;

internal static unsafe class VideoToolboxNative
{
    internal const string VideoToolbox = "/System/Library/Frameworks/VideoToolbox.framework/VideoToolbox";
    internal const string CoreMedia = "/System/Library/Frameworks/CoreMedia.framework/CoreMedia";
    internal const string CoreVideo = "/System/Library/Frameworks/CoreVideo.framework/CoreVideo";
    internal const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    internal const uint CodecTypeH264 = 0x61766331;
    internal const uint CodecTypeVp9 = 0x76703039;
    internal const uint CodecTypeAv1 = 0x61763031;
    internal const uint PixelFormat32Bgra = 0x42475241;
    internal const uint StringEncodingUtf8 = 0x08000100;
    internal const nint NumberSInt32Type = 3;
    internal const ulong PixelBufferLockReadOnly = 1;
    internal const uint TimeFlagsValid = 1;

    private static readonly object Gate = new();
    private static bool _probed;
    private static string? _whyNot;

    internal static nint TypeDictionaryKeyCallBacks { get; private set; }

    internal static nint TypeDictionaryValueCallBacks { get; private set; }

    internal static nint AllocatorNull { get; private set; }

    internal static nint BooleanFalse { get; private set; }

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
        if (!NativeLibrary.TryLoad(CoreFoundation, out var coreFoundation))
        {
            return "CoreFoundation.framework does not load on this host";
        }

        if (!NativeLibrary.TryLoad(CoreMedia, out _))
        {
            return "CoreMedia.framework does not load on this host";
        }

        if (!NativeLibrary.TryLoad(CoreVideo, out _))
        {
            return "CoreVideo.framework does not load on this host";
        }

        if (!NativeLibrary.TryLoad(VideoToolbox, out var videoToolbox))
        {
            return "VideoToolbox.framework does not load on this host";
        }

        if (!NativeLibrary.TryGetExport(videoToolbox, "VTDecompressionSessionCreate", out _))
        {
            return "the VideoToolbox on this host exports no VTDecompressionSessionCreate";
        }

        try
        {
            TypeDictionaryKeyCallBacks = NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryKeyCallBacks");
            TypeDictionaryValueCallBacks = NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryValueCallBacks");
            AllocatorNull = *(nint*)NativeLibrary.GetExport(coreFoundation, "kCFAllocatorNull");
            BooleanFalse = *(nint*)NativeLibrary.GetExport(coreFoundation, "kCFBooleanFalse");
        }
        catch (EntryPointNotFoundException missing)
        {
            return $"the CoreFoundation on this host misses a symbol: {missing.Message}";
        }

        Log.Debug($"decode over VideoToolbox.framework");
        return null;
    }

    internal static bool IsHardwareDecodeSupported(uint codecType)
    {
        try
        {
            return VTIsHardwareDecodeSupported(codecType) != 0;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    internal static void RegisterSupplementalDecoder(uint codecType)
    {
        try
        {
            VTRegisterSupplementalVideoDecoderIfAvailable(codecType);
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    internal static nint CreateString(string text)
    {
        Span<byte> bytes = stackalloc byte[64];
        var written = System.Text.Encoding.UTF8.GetBytes(text, bytes[..^1]);
        bytes[written] = 0;
        fixed (byte* cString = bytes)
        {
            return CFStringCreateWithCString(0, cString, StringEncodingUtf8);
        }
    }

    internal static nint CreateNumber(int value) => CFNumberCreate(0, NumberSInt32Type, &value);

    internal static nint CreateDictionary(ReadOnlySpan<nint> keys, ReadOnlySpan<nint> values)
    {
        fixed (nint* keyPointer = keys)
        fixed (nint* valuePointer = values)
        {
            return CFDictionaryCreate(
                0, keyPointer, valuePointer, keys.Length, TypeDictionaryKeyCallBacks, TypeDictionaryValueCallBacks);
        }
    }

    internal static nint CreateData(ReadOnlySpan<byte> bytes)
    {
        fixed (byte* pointer = bytes)
        {
            return CFDataCreate(0, pointer, bytes.Length);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CMTime
    {
        public long Value;

        public int Timescale;

        public uint Flags;

        public long Epoch;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CMSampleTimingInfo
    {
        public CMTime Duration;

        public CMTime PresentationTimeStamp;

        public CMTime DecodeTimeStamp;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VTDecompressionOutputCallbackRecord
    {
        public nint Callback;

        public nint RefCon;
    }

    [DllImport(CoreFoundation)]
    internal static extern void CFRelease(nint reference);

    [DllImport(CoreFoundation)]
    private static extern nint CFStringCreateWithCString(nint allocator, byte* cString, uint encoding);

    [DllImport(CoreFoundation)]
    private static extern nint CFNumberCreate(nint allocator, nint type, void* valuePointer);

    [DllImport(CoreFoundation)]
    private static extern nint CFDictionaryCreate(
        nint allocator, nint* keys, nint* values, nint count, nint keyCallBacks, nint valueCallBacks);

    [DllImport(CoreFoundation)]
    private static extern nint CFDataCreate(nint allocator, byte* bytes, nint length);

    [DllImport(CoreMedia)]
    internal static extern int CMVideoFormatDescriptionCreate(
        nint allocator, uint codecType, int width, int height, nint extensions, nint* formatDescriptionOut);

    [DllImport(CoreMedia)]
    internal static extern int CMBlockBufferCreateWithMemoryBlock(
        nint structureAllocator,
        void* memoryBlock,
        nuint blockLength,
        nint blockAllocator,
        nint customBlockSource,
        nuint offsetToData,
        nuint dataLength,
        uint flags,
        nint* blockBufferOut);

    [DllImport(CoreMedia)]
    internal static extern int CMSampleBufferCreateReady(
        nint allocator,
        nint dataBuffer,
        nint formatDescription,
        nint numSamples,
        nint numSampleTimingEntries,
        CMSampleTimingInfo* sampleTimingArray,
        nint numSampleSizeEntries,
        nuint* sampleSizeArray,
        nint* sampleBufferOut);

    [DllImport(VideoToolbox)]
    internal static extern int VTDecompressionSessionCreate(
        nint allocator,
        nint videoFormatDescription,
        nint videoDecoderSpecification,
        nint destinationImageBufferAttributes,
        VTDecompressionOutputCallbackRecord* outputCallback,
        nint* decompressionSessionOut);

    [DllImport(VideoToolbox)]
    internal static extern int VTDecompressionSessionDecodeFrame(
        nint session, nint sampleBuffer, uint decodeFlags, nint sourceFrameRefCon, uint* infoFlagsOut);

    [DllImport(VideoToolbox)]
    internal static extern int VTDecompressionSessionWaitForAsynchronousFrames(nint session);

    [DllImport(VideoToolbox)]
    internal static extern byte VTDecompressionSessionCanAcceptFormatDescription(nint session, nint formatDescription);

    [DllImport(VideoToolbox)]
    internal static extern void VTDecompressionSessionInvalidate(nint session);

    [DllImport(VideoToolbox)]
    private static extern byte VTIsHardwareDecodeSupported(uint codecType);

    [DllImport(VideoToolbox)]
    private static extern int VTRegisterSupplementalVideoDecoderIfAvailable(uint codecType);

    [DllImport(CoreVideo)]
    internal static extern int CVPixelBufferLockBaseAddress(nint pixelBuffer, ulong lockFlags);

    [DllImport(CoreVideo)]
    internal static extern int CVPixelBufferUnlockBaseAddress(nint pixelBuffer, ulong unlockFlags);

    [DllImport(CoreVideo)]
    internal static extern void* CVPixelBufferGetBaseAddress(nint pixelBuffer);

    [DllImport(CoreVideo)]
    internal static extern nuint CVPixelBufferGetBytesPerRow(nint pixelBuffer);

    [DllImport(CoreVideo)]
    internal static extern nuint CVPixelBufferGetWidth(nint pixelBuffer);

    [DllImport(CoreVideo)]
    internal static extern nuint CVPixelBufferGetHeight(nint pixelBuffer);

    [DllImport(CoreVideo)]
    internal static extern uint CVPixelBufferGetPixelFormatType(nint pixelBuffer);
}
