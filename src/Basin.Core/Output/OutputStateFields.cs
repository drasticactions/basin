using Pixman;

namespace Basin;

[Flags]
public enum OutputStateFields
{
    None = 0,
    Enabled = 1 << 0,
    Mode = 1 << 1,
    Scale = 1 << 2,
    Transform = 1 << 3,
    Buffer = 1 << 4,
    Damage = 1 << 5,
    AdaptiveSync = 1 << 6,
    InFence = 1 << 7,
    OutFence = 1 << 8,
    Tearing = 1 << 9,
    Hdr = 1 << 10,
    Layers = 1 << 11,
    GammaLut = 1 << 12,
    Ctm = 1 << 13,
    DegammaLut = 1 << 14,
}
