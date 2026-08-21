using Pixman;

namespace Basin.Scene;

public readonly record struct FrameTick(long TargetPresentNanos, long RefreshIntervalNanos);
