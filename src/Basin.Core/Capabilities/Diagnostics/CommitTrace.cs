namespace Basin.Capabilities;

public readonly record struct CommitTrace(
    string Kind,
    Surface Surface,
    int Width,
    int Height,
    DrmFormat Format,
    IReadOnlyList<Box> Damage,
    int FrameCallbacks,
    bool Feedback,
    uint TimestampMs);
