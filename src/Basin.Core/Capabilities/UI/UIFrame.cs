using Pixman;

namespace Basin.Capabilities;

/// <summary>
/// One drawn frame of an <see cref="IUISurface"/>: a lock on its buffer, the damage and an optional acquire fence.
/// The holder must dispose it once, which releases the lock and closes the fence.
/// </summary>
public struct UIFrame : IDisposable
{
    private BufferLock _lock;
    private int _acquireFencePlusOne;

    /// <summary>
    /// Initializes a new instance of the <see cref="UIFrame"/> struct. The frame has no acquire fence.
    /// </summary>
    /// <param name="bufferLock">The lock on the drawn buffer. The frame takes ownership of it.</param>
    /// <param name="damage">The changed region, or <see langword="null"/> when the whole buffer can have changed.</param>
    public UIFrame(BufferLock bufferLock, PixmanRegion32? damage)
        : this(bufferLock, damage, acquireFenceFd: -1)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UIFrame"/> struct.
    /// </summary>
    /// <param name="bufferLock">The lock on the drawn buffer. The frame takes ownership of it.</param>
    /// <param name="damage">The changed region, or <see langword="null"/> when the whole buffer can have changed.</param>
    /// <param name="acquireFenceFd">A sync file that signals when drawing ends, or a negative value for none. The frame takes ownership of it.</param>
    public UIFrame(BufferLock bufferLock, PixmanRegion32? damage, int acquireFenceFd)
    {
        _lock = bufferLock;
        Damage = damage;
        _acquireFencePlusOne = acquireFenceFd < 0 ? 0 : acquireFenceFd + 1;
    }

    /// <summary>
    /// Gets the drawn buffer, or <see langword="null"/> for a default frame.
    /// </summary>
    public readonly IBuffer? Buffer => _lock.Buffer;

    /// <summary>
    /// Gets the changed region, or <see langword="null"/> when the whole buffer can have changed.
    /// </summary>
    public PixmanRegion32? Damage { get; }

    /// <summary>
    /// Gets the sync file that signals when drawing ends, or -1 when the buffer is ready. The frame keeps ownership of the fd.
    /// </summary>
    public readonly int AcquireFenceFd => _acquireFencePlusOne - 1;

    /// <summary>
    /// Closes the acquire fence and releases the buffer lock.
    /// </summary>
    public void Dispose()
    {
        RenderFences.CloseFence(_acquireFencePlusOne - 1);
        _acquireFencePlusOne = 0;
        _lock.Dispose();
    }
}
