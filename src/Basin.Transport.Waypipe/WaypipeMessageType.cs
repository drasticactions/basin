using System.Buffers.Binary;

namespace Basin.Transport.Waypipe;

public enum WaypipeMessageType
{
    Protocol = 0,

    InjectRIDs = 1,

    OpenFile = 2,

    ExtendFile = 3,

    OpenDmabuf = 4,

    BufferFill = 5,

    BufferDiff = 6,

    OpenIRPipe = 7,

    OpenIWPipe = 8,

    OpenRWPipe = 9,

    PipeTransfer = 10,

    PipeShutdownR = 11,

    PipeShutdownW = 12,

    OpenDmaVidSrc = 13,

    OpenDmaVidDst = 14,

    SendDmaVidPacket = 15,

    AckNblocks = 16,

    Restart = 17,

    Close = 18,

    OpenDmaVidSrcV2 = 19,

    OpenDmaVidDstV2 = 20,

    OpenTimeline = 21,

    SignalTimeline = 22,

    Version = 23,
}
