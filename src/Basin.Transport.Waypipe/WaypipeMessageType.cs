using System.Buffers.Binary;

namespace Basin.Transport.Waypipe;

/// <summary>
/// The type field of a waypipe frame header. The values match the waypipe wire format.
/// </summary>
public enum WaypipeMessageType
{
    /// <summary>
    /// Wayland protocol bytes, with descriptor counts tagged in each message header.
    /// </summary>
    Protocol = 0,

    /// <summary>
    /// The remote ids of the descriptors that the next protocol messages carry.
    /// </summary>
    InjectRIDs = 1,

    /// <summary>
    /// Creates a shared memory region for a remote id.
    /// </summary>
    OpenFile = 2,

    /// <summary>
    /// Grows the shared memory region of a remote id.
    /// </summary>
    ExtendFile = 3,

    /// <summary>
    /// Creates a linear image for a remote dmabuf.
    /// </summary>
    OpenDmabuf = 4,

    /// <summary>
    /// Writes a contiguous span of buffer contents.
    /// </summary>
    BufferFill = 5,

    /// <summary>
    /// Writes a set of changed blocks into buffer contents.
    /// </summary>
    BufferDiff = 6,

    /// <summary>
    /// Creates a pipe that the receiving side reads.
    /// </summary>
    OpenIRPipe = 7,

    /// <summary>
    /// Creates a pipe that the receiving side writes.
    /// </summary>
    OpenIWPipe = 8,

    /// <summary>
    /// Creates a pipe that the receiving side reads and writes.
    /// </summary>
    OpenRWPipe = 9,

    /// <summary>
    /// Carries bytes through a pipe.
    /// </summary>
    PipeTransfer = 10,

    /// <summary>
    /// Closes the read direction of a pipe.
    /// </summary>
    PipeShutdownR = 11,

    /// <summary>
    /// Closes the write direction of a pipe.
    /// </summary>
    PipeShutdownW = 12,

    /// <summary>
    /// Opens a video source in the legacy waypipe-c format. This transport refuses it.
    /// </summary>
    OpenDmaVidSrc = 13,

    /// <summary>
    /// Opens a video destination in the legacy waypipe-c format. This transport refuses it.
    /// </summary>
    OpenDmaVidDst = 14,

    /// <summary>
    /// Carries one encoded video packet for a video stream.
    /// </summary>
    SendDmaVidPacket = 15,

    /// <summary>
    /// Acknowledges received blocks. This transport ignores it.
    /// </summary>
    AckNblocks = 16,

    /// <summary>
    /// Asks to resume a session after a reconnect. This transport refuses it.
    /// </summary>
    Restart = 17,

    /// <summary>
    /// Ends the channel in an orderly way.
    /// </summary>
    Close = 18,

    /// <summary>
    /// Asks the receiving side to encode a video stream. This transport refuses it.
    /// </summary>
    OpenDmaVidSrcV2 = 19,

    /// <summary>
    /// Opens a video stream that the receiving side decodes into an image.
    /// </summary>
    OpenDmaVidDstV2 = 20,

    /// <summary>
    /// Opens an explicit-sync timeline. This transport refuses it.
    /// </summary>
    OpenTimeline = 21,

    /// <summary>
    /// Signals an explicit-sync timeline point. This transport refuses it.
    /// </summary>
    SignalTimeline = 22,

    /// <summary>
    /// Carries the wire version that the sender selected.
    /// </summary>
    Version = 23,
}
