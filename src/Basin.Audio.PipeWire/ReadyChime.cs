namespace Basin.Audio.PipeWire;

/// <summary>
/// A short rising chime that a compositor can play when it is ready, for example to tell a blind user that the screen reader can start.
/// It is synthesized, so it needs no sound file.
/// </summary>
public static class ReadyChime
{
    /// <summary>
    /// Makes the chime: two sine tones a fifth apart, 0.3 seconds in all, with soft edges.
    /// </summary>
    /// <returns>The clip.</returns>
    public static SoundClip Create()
    {
        var first = Tone(660, 0.12);
        var second = Tone(990, 0.18);
        var samples = new float[first.Length + second.Length];
        first.CopyTo(samples, 0);
        second.CopyTo(samples, first.Length);
        return new SoundClip("ready-chime", samples);
    }

    private static float[] Tone(double hertz, double seconds)
    {
        var count = (int)(SoundClip.Rate * seconds);
        var fade = SoundClip.Rate / 100;
        var samples = new float[count];
        for (var i = 0; i < count; i++)
        {
            var edge = Math.Min(1.0, Math.Min(i, count - 1 - i) / (double)fade);
            samples[i] = (float)(0.25 * edge * Math.Sin(2 * Math.PI * hertz * i / SoundClip.Rate));
        }

        return samples;
    }
}
