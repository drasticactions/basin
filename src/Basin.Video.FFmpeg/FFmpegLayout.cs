using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Basin.Diagnostics;

namespace Basin.Video.FFmpeg;

internal readonly record struct FFmpegLayout(
    int FrameData,
    int FrameLinesize,
    int FrameWidth,
    int FrameHeight,
    int FrameFormat,
    int PacketData,
    int PacketSize,
    int ContextGetFormat,
    int ContextHwDeviceCtx);
