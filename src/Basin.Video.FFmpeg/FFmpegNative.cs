using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Basin.Diagnostics;

namespace Basin.Video.FFmpeg;

internal static unsafe class FFmpegNative
{
    private const int LogWarning = 24;
    private const int LogError = 16;

    private static readonly object Gate = new();
    private static bool _probed;
    private static string? _whyNot;

    internal static FFmpegLayout Layout { get; private set; }

    internal static delegate* unmanaged[Cdecl]<uint> avcodec_version;
    internal static delegate* unmanaged[Cdecl]<byte*, nint> avcodec_descriptor_get_by_name;
    internal static delegate* unmanaged[Cdecl]<int, nint> avcodec_find_decoder;
    internal static delegate* unmanaged[Cdecl]<nint, nint> avcodec_alloc_context3;
    internal static delegate* unmanaged[Cdecl]<nint, nint, nint*, int> avcodec_open2;
    internal static delegate* unmanaged[Cdecl]<nint*, void> avcodec_free_context;
    internal static delegate* unmanaged[Cdecl]<nint, nint, int> avcodec_send_packet;
    internal static delegate* unmanaged[Cdecl]<nint, nint, int> avcodec_receive_frame;
    internal static delegate* unmanaged[Cdecl]<nint, void> avcodec_flush_buffers;
    internal static delegate* unmanaged[Cdecl]<nint> av_packet_alloc;
    internal static delegate* unmanaged[Cdecl]<nint*, void> av_packet_free;

    internal static delegate* unmanaged[Cdecl]<uint> avutil_version;
    internal static delegate* unmanaged[Cdecl]<nint> av_frame_alloc;
    internal static delegate* unmanaged[Cdecl]<nint*, void> av_frame_free;
    internal static delegate* unmanaged[Cdecl]<nint, void> av_frame_unref;
    internal static delegate* unmanaged[Cdecl]<int, byte*, nuint, int> av_strerror;
    internal static delegate* unmanaged[Cdecl]<nint*, byte*, byte*, int, int> av_dict_set;
    internal static delegate* unmanaged[Cdecl]<nint*, void> av_dict_free;
    internal static delegate* unmanaged[Cdecl]<int, void> av_log_set_level;
    internal static delegate* unmanaged[Cdecl]<nint, void> av_log_set_callback;
    internal static delegate* unmanaged[Cdecl]<nint, int, byte*, byte*, byte*, int, int*, void> av_log_format_line;
    internal static delegate* unmanaged[Cdecl]<byte*, int> av_get_pix_fmt;
    internal static delegate* unmanaged[Cdecl]<byte*, int> av_hwdevice_find_type_by_name;
    internal static delegate* unmanaged[Cdecl]<nint*, int, byte*, nint, int, int> av_hwdevice_ctx_create;
    internal static delegate* unmanaged[Cdecl]<nint, nint, int, int> av_hwframe_transfer_data;
    internal static delegate* unmanaged[Cdecl]<nint, nint> av_buffer_ref;
    internal static delegate* unmanaged[Cdecl]<nint*, void> av_buffer_unref;

    internal static delegate* unmanaged[Cdecl]<uint> swscale_version;
    internal static delegate* unmanaged[Cdecl]<nint, int, int, int, int, int, int, int, nint, nint, double*, nint> sws_getCachedContext;
    internal static delegate* unmanaged[Cdecl]<nint, byte**, int*, int, int, byte**, int*, int> sws_scale;
    internal static delegate* unmanaged[Cdecl]<nint, void> sws_freeContext;
    internal static delegate* unmanaged[Cdecl]<nint, int*, int, int*, int, int, int, int, int> sws_setColorspaceDetails;
    internal static delegate* unmanaged[Cdecl]<int, int*> sws_getCoefficients;

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
        if (!TryLoadLibrary("avcodec", [63, 62, 61, 60], out var avcodec, out var avcodecName))
        {
            return "no libavcodec with a known major (60-63) loads on this host";
        }

        if (!TryLoadLibrary("avutil", [61, 60, 59, 58], out var avutil, out var avutilName))
        {
            return "no libavutil with a known major (58-61) loads on this host";
        }

        if (!TryLoadLibrary("swscale", [10, 9, 8, 7], out var swscale, out var swscaleName))
        {
            return "no libswscale with a known major (7-10) loads on this host";
        }

        try
        {
            avcodec_version = (delegate* unmanaged[Cdecl]<uint>)NativeLibrary.GetExport(avcodec, "avcodec_version");
            avcodec_descriptor_get_by_name = (delegate* unmanaged[Cdecl]<byte*, nint>)NativeLibrary.GetExport(avcodec, "avcodec_descriptor_get_by_name");
            avcodec_find_decoder = (delegate* unmanaged[Cdecl]<int, nint>)NativeLibrary.GetExport(avcodec, "avcodec_find_decoder");
            avcodec_alloc_context3 = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(avcodec, "avcodec_alloc_context3");
            avcodec_open2 = (delegate* unmanaged[Cdecl]<nint, nint, nint*, int>)NativeLibrary.GetExport(avcodec, "avcodec_open2");
            avcodec_free_context = (delegate* unmanaged[Cdecl]<nint*, void>)NativeLibrary.GetExport(avcodec, "avcodec_free_context");
            avcodec_send_packet = (delegate* unmanaged[Cdecl]<nint, nint, int>)NativeLibrary.GetExport(avcodec, "avcodec_send_packet");
            avcodec_receive_frame = (delegate* unmanaged[Cdecl]<nint, nint, int>)NativeLibrary.GetExport(avcodec, "avcodec_receive_frame");
            avcodec_flush_buffers = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(avcodec, "avcodec_flush_buffers");
            av_packet_alloc = (delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(avcodec, "av_packet_alloc");
            av_packet_free = (delegate* unmanaged[Cdecl]<nint*, void>)NativeLibrary.GetExport(avcodec, "av_packet_free");

            avutil_version = (delegate* unmanaged[Cdecl]<uint>)NativeLibrary.GetExport(avutil, "avutil_version");
            av_frame_alloc = (delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(avutil, "av_frame_alloc");
            av_frame_free = (delegate* unmanaged[Cdecl]<nint*, void>)NativeLibrary.GetExport(avutil, "av_frame_free");
            av_frame_unref = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(avutil, "av_frame_unref");
            av_strerror = (delegate* unmanaged[Cdecl]<int, byte*, nuint, int>)NativeLibrary.GetExport(avutil, "av_strerror");
            av_dict_set = (delegate* unmanaged[Cdecl]<nint*, byte*, byte*, int, int>)NativeLibrary.GetExport(avutil, "av_dict_set");
            av_dict_free = (delegate* unmanaged[Cdecl]<nint*, void>)NativeLibrary.GetExport(avutil, "av_dict_free");
            av_log_set_level = (delegate* unmanaged[Cdecl]<int, void>)NativeLibrary.GetExport(avutil, "av_log_set_level");
            av_log_set_callback = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(avutil, "av_log_set_callback");
            av_log_format_line = (delegate* unmanaged[Cdecl]<nint, int, byte*, byte*, byte*, int, int*, void>)NativeLibrary.GetExport(avutil, "av_log_format_line");
            av_get_pix_fmt = (delegate* unmanaged[Cdecl]<byte*, int>)NativeLibrary.GetExport(avutil, "av_get_pix_fmt");
            av_hwdevice_find_type_by_name = (delegate* unmanaged[Cdecl]<byte*, int>)NativeLibrary.GetExport(avutil, "av_hwdevice_find_type_by_name");
            av_hwdevice_ctx_create = (delegate* unmanaged[Cdecl]<nint*, int, byte*, nint, int, int>)NativeLibrary.GetExport(avutil, "av_hwdevice_ctx_create");
            av_hwframe_transfer_data = (delegate* unmanaged[Cdecl]<nint, nint, int, int>)NativeLibrary.GetExport(avutil, "av_hwframe_transfer_data");
            av_buffer_ref = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(avutil, "av_buffer_ref");
            av_buffer_unref = (delegate* unmanaged[Cdecl]<nint*, void>)NativeLibrary.GetExport(avutil, "av_buffer_unref");

            swscale_version = (delegate* unmanaged[Cdecl]<uint>)NativeLibrary.GetExport(swscale, "swscale_version");
            sws_getCachedContext = (delegate* unmanaged[Cdecl]<nint, int, int, int, int, int, int, int, nint, nint, double*, nint>)NativeLibrary.GetExport(swscale, "sws_getCachedContext");
            sws_scale = (delegate* unmanaged[Cdecl]<nint, byte**, int*, int, int, byte**, int*, int>)NativeLibrary.GetExport(swscale, "sws_scale");
            sws_freeContext = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(swscale, "sws_freeContext");
            sws_setColorspaceDetails = (delegate* unmanaged[Cdecl]<nint, int*, int, int*, int, int, int, int, int>)NativeLibrary.GetExport(swscale, "sws_setColorspaceDetails");
            sws_getCoefficients = (delegate* unmanaged[Cdecl]<int, int*>)NativeLibrary.GetExport(swscale, "sws_getCoefficients");
        }
        catch (EntryPointNotFoundException missing)
        {
            return $"the FFmpeg libraries on this host miss an entry point: {missing.Message}";
        }

        var avcodecMajor = (int)(avcodec_version() >> 16);
        var avutilMajor = (int)(avutil_version() >> 16);
        var swscaleMajor = (int)(swscale_version() >> 16);
        if (avcodecMajor is < 60 or > 63 || avutilMajor is < 58 or > 61 || swscaleMajor is < 7 or > 10)
        {
            return $"the loaded FFmpeg majors (avcodec {avcodecMajor}, avutil {avutilMajor}, swscale {swscaleMajor}) "
                + "are outside the range this build's struct layouts were written against";
        }

        Layout = avcodecMajor == 60
            ? new FFmpegLayout(
                FrameData: 0,
                FrameLinesize: 64,
                FrameWidth: 104,
                FrameHeight: 108,
                FrameFormat: 116,
                PacketData: 24,
                PacketSize: 32,
                ContextGetFormat: 152,
                ContextHwDeviceCtx: 864)
            : new FFmpegLayout(
                FrameData: 0,
                FrameLinesize: 64,
                FrameWidth: 104,
                FrameHeight: 108,
                FrameFormat: 116,
                PacketData: 24,
                PacketSize: 32,
                ContextGetFormat: 192,
                ContextHwDeviceCtx: 560);

        av_log_set_level(LogWarning);
        av_log_set_callback((nint)(delegate* unmanaged[Cdecl]<nint, int, byte*, byte*, void>)&LogCallback);
        BasinLog.Debug(
            $"ffmpeg: decode over {avcodecName} {avcodecMajor}, {avutilName} {avutilMajor}, {swscaleName} {swscaleMajor}");
        return null;
    }

    private static bool TryLoadLibrary(string stem, ReadOnlySpan<int> majors, out nint handle, out string name)
    {
        foreach (var major in majors)
        {
            string[] candidates = OperatingSystem.IsWindows()
                ? [$"{stem}-{major}.dll"]
                : OperatingSystem.IsMacOS()
                    ? [$"lib{stem}.{major}.dylib", $"/opt/homebrew/lib/lib{stem}.{major}.dylib", $"/usr/local/lib/lib{stem}.{major}.dylib"]
                    : [$"lib{stem}.so.{major}"];
            foreach (var candidate in candidates)
            {
                if (NativeLibrary.TryLoad(candidate, out handle))
                {
                    name = candidate;
                    return true;
                }
            }
        }

        handle = 0;
        name = string.Empty;
        return false;
    }

    internal static int PixelFormat(string name)
    {
        Span<byte> bytes = stackalloc byte[32];
        var written = System.Text.Encoding.ASCII.GetBytes(name, bytes[..^1]);
        bytes[written] = 0;
        fixed (byte* text = bytes)
        {
            return av_get_pix_fmt(text);
        }
    }

    internal static int CodecId(string name)
    {
        Span<byte> bytes = stackalloc byte[32];
        var written = System.Text.Encoding.ASCII.GetBytes(name, bytes[..^1]);
        bytes[written] = 0;
        fixed (byte* text = bytes)
        {
            var descriptor = avcodec_descriptor_get_by_name(text);
            return descriptor == 0 ? -1 : *(int*)descriptor;
        }
    }

    internal static string DescribeError(int error)
    {
        Span<byte> buffer = stackalloc byte[256];
        fixed (byte* text = buffer)
        {
            return av_strerror(error, text, (nuint)buffer.Length) == 0
                ? Marshal.PtrToStringUTF8((nint)text) ?? error.ToString()
                : error.ToString();
        }
    }

    internal static int HardwarePixelFormat = int.MinValue;

    internal static nint ChooseFormatPointer =>
        (nint)(delegate* unmanaged[Cdecl]<nint, nint, int>)&ChooseFormat;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ChooseFormat(nint context, nint formats)
    {
        try
        {
            var list = (int*)formats;
            var fallback = -1;
            for (var i = 0; list[i] != -1; i++)
            {
                if (list[i] == HardwarePixelFormat)
                {
                    return list[i];
                }

                fallback = list[i];
            }

            return fallback;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void LogCallback(nint context, int level, byte* format, byte* arguments)
    {
        try
        {
            if (level > LogWarning)
            {
                return;
            }

            var line = stackalloc byte[1024];
            var printPrefix = 1;
            av_log_format_line(context, level, format, arguments, line, 1024, &printPrefix);
            var text = Marshal.PtrToStringUTF8((nint)line)?.TrimEnd('\n');
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (level <= LogError)
            {
                BasinLog.Error($"ffmpeg: {text}");
            }
            else
            {
                BasinLog.Warn($"ffmpeg: {text}");
            }
        }
        catch (Exception)
        {
        }
    }
}
