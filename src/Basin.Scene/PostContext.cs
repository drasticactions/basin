using Pixman;

namespace Basin.Scene;

public readonly record struct PostContext(int Width, int Height, FrameTick Tick, PixmanRegion32? Damage = null);
