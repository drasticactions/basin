namespace Basin;

public partial interface IBuffer
{
    int Width { get; }

    int Height { get; }

    int LockCount { get; }

    DrmFormat Format { get; }

    bool IsDestroyed { get; }

    BufferLock Lock();

    void Unlock();

    event Action? Released;

    event Action? Destroyed;

    bool BeginDataAccess(BufferDataAccess access, out BufferDataView view);

    void EndDataAccess();
}
