namespace Basin.Frames.Metacity;

internal sealed class MetacityAlphaSpec(byte[] alphas)
{
    public byte[] Alphas { get; } = alphas;

    public bool NeedsAlpha => Alphas.Length > 1 || Alphas[0] != 0xFF;
}
