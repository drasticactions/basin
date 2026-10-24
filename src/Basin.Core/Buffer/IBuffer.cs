namespace Basin;

/// <summary>
/// A pixel buffer with an explicit lock count. Consumers lock it while they use it, and the owner must not reclaim it before the count returns to zero.
/// </summary>
public partial interface IBuffer
{
    /// <summary>Gets the width in physical pixels.</summary>
    int Width { get; }

    /// <summary>Gets the height in physical pixels.</summary>
    int Height { get; }

    /// <summary>Gets the number of locks that are not yet released.</summary>
    int LockCount { get; }

    /// <summary>
    /// Gets the pixel format. It is <see cref="DrmFormat.Invalid"/> when the buffer has no single known format.
    /// </summary>
    DrmFormat Format { get; }

    /// <summary>
    /// Gets a value indicating whether the producer destroyed the buffer. A destroyed buffer stays readable while a lock holds its storage.
    /// </summary>
    bool IsDestroyed { get; }

    /// <summary>
    /// Adds one lock. The storage stays valid until each lock is released.
    /// </summary>
    /// <returns>A lock that calls <see cref="Unlock"/> when it is disposed.</returns>
    BufferLock Lock();

    /// <summary>
    /// Removes one lock. When the count reaches zero, the buffer raises <see cref="Released"/>.
    /// </summary>
    void Unlock();

    /// <summary>
    /// Occurs when the lock count drops to zero. Handlers run on the compositor thread inside <see cref="Unlock"/>.
    /// </summary>
    event Action? Released;

    /// <summary>
    /// Occurs once, when the producer destroys the buffer. Locks can still hold the storage after this event.
    /// </summary>
    event Action? Destroyed;

    /// <summary>
    /// Maps the pixels for CPU access. Each successful call must be paired with one <see cref="EndDataAccess"/>.
    /// </summary>
    /// <param name="access">The kinds of access that the caller needs.</param>
    /// <param name="view">The mapped pixels, or the default value when the call fails.</param>
    /// <returns><see langword="true"/> when the pixels are mapped, or <see langword="false"/> when the buffer has no CPU path for <paramref name="access"/>.</returns>
    bool BeginDataAccess(BufferDataAccess access, out BufferDataView view);

    /// <summary>
    /// Ends the access that the matching <see cref="BeginDataAccess"/> started. The view from that call is not valid after this call.
    /// </summary>
    void EndDataAccess();

    /// <summary>
    /// Gets the number of write accesses granted so far. A consumer that caches the pixels compares it to detect CPU writes. The default is always zero.
    /// </summary>
    ulong WriteCount => 0;
}
