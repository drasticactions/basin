using System.Numerics;

namespace DungeonWm;

internal sealed record BakeSettings(
    float Unit = 0.75f,
    RoofKind Roof = RoofKind.Stone,
    bool RoofBeams = true,
    float TorchHeight = 2.1f,
    Vector3 TorchColor = default,
    double Props = 0.5,
    bool DynamicProps = true,
    int Seed = 7,
    float TorchFlicker = 0.12f)
{
    public Vector3 Torch => TorchColor == default ? new Vector3(1.0f, 0.62f, 0.3f) : TorchColor;
}
