namespace Basin.Audio.PipeWire;

/// <summary>
/// The placement of a <see cref="SpatialVoice"/>. <see cref="SpatialVoice.Update"/> quantizes each value and sends only the controls that change.
/// </summary>
/// <param name="Azimuth">The direction in degrees, counterclockwise from straight ahead, so 90 is to the left. The quantum is 2 degrees.</param>
/// <param name="Elevation">The elevation in degrees, from -90 to 90. Only an HRTF voice uses it.</param>
/// <param name="Radius">The distance in meters. Only an HRTF voice uses it, clamped to the radius range of its file.</param>
/// <param name="GainDb">The dry gain in decibels. A value at or below <see cref="SilentDb"/> silences the voice.</param>
/// <param name="CutoffHz">The occlusion low-pass cutoff in hertz, at most <see cref="OpenCutoffHz"/>.</param>
/// <param name="WetDb">The reverb level in decibels. It has an effect only when the graph has an impulse file.</param>
public readonly record struct VoiceParams(
    float Azimuth,
    float Elevation,
    float Radius,
    float GainDb,
    float CutoffHz,
    float WetDb)
{
    /// <summary>
    /// The level in decibels at or below which a gain counts as silence.
    /// </summary>
    public const float SilentDb = -90;

    /// <summary>
    /// The low-pass cutoff in hertz that leaves the signal unfiltered. No cutoff can be higher.
    /// </summary>
    public const float OpenCutoffHz = 20000;

    /// <summary>
    /// Gets a source straight ahead at 1 m, at 0 dB, with no filter and no reverb.
    /// </summary>
    public static VoiceParams Centered => new(0, 0, 1, 0, OpenCutoffHz, SilentDb);
}
