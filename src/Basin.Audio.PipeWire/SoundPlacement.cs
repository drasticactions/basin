namespace Basin.Audio.PipeWire;

/// <summary>
/// Places one sound effect that <see cref="SoundPlayer.Play"/> mixes. The player applies a gain, a constant-power pan and a one-pole low-pass to the clip.
/// </summary>
/// <param name="GainDb">The gain in decibels. The player caps it at 20 dB and refuses a sound at or below <see cref="VoiceParams.SilentDb"/>.</param>
/// <param name="Azimuth">The direction in degrees, counterclockwise from straight ahead, so 90 is to the left. A source behind pans like a source in front.</param>
/// <param name="CutoffHz">The low-pass cutoff in hertz, clamped to 20 through <see cref="VoiceParams.OpenCutoffHz"/>. The open cutoff turns the filter off.</param>
/// <param name="Rate">The playback speed, clamped to 0.25 through 4. A value above 1 plays faster and at a higher pitch.</param>
public readonly record struct SoundPlacement(float GainDb, float Azimuth = 0, float CutoffHz = VoiceParams.OpenCutoffHz, float Rate = 1)
{
    /// <summary>
    /// Gets a placement straight ahead at 0 dB, with no filter and the original speed.
    /// </summary>
    public static SoundPlacement Centered => new(0);
}
