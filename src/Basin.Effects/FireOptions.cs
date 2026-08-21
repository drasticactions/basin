using Basin.Scene;

namespace Basin.Effects;

public readonly record struct FireOptions
{
    public FireOptions()
    {
    }

    public int ParticleCount { get; init; } = 2000;

    public float ParticleSize { get; init; } = 16f;

    public RenderColor Color { get; init; } = new(0.698f, 0.137f, 0.012f, 1f);

    public bool RandomColor { get; init; }

    public int Padding { get; init; } = 200;

    public ulong Seed { get; init; } = 0x2545F4914F6CDD1DUL;
}
