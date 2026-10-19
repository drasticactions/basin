namespace Basin.Sdl;

public readonly record struct SdlHdrState(bool Enabled, float SdrWhiteNits, float Headroom)
{
    public static SdlHdrState Sdr => new(false, 203f, 1f);

    public float PeakNits => SdrWhiteNits * Headroom;
}
