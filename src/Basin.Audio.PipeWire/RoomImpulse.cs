using System.Globalization;

namespace Basin.Audio.PipeWire;

/// <summary>
/// Makes synthetic stereo room impulse responses for the convolver of a <see cref="VoiceGraph"/>. A response needs no asset or license.
/// </summary>
public static class RoomImpulse
{
    /// <summary>
    /// The sample rate in hertz of every impulse file.
    /// </summary>
    public const int Rate = 48000;

    /// <summary>
    /// The step in seconds to which <see cref="PathFor"/> rounds the reverb time. It is also the smallest reverb time.
    /// </summary>
    public const float SecondsQuantum = 0.05f;

    /// <summary>
    /// Gets the path of a cached 32-bit float stereo WAV for the quantized parameters. The method writes the file first when it does not exist, through a temporary file and a rename.
    /// </summary>
    /// <param name="rt60Seconds">The time in seconds for the low band to decay by 60 dB. It is rounded to <see cref="SecondsQuantum"/>.</param>
    /// <param name="preDelaySeconds">The silence before the response in seconds, clamped to 0 through 0.2 and rounded to 1 ms.</param>
    /// <param name="crossoverHz">The frequency in hertz that splits the low and high bands, clamped to 200 through 16000.</param>
    /// <param name="highDecayRatio">The high-band reverb time as a fraction of <paramref name="rt60Seconds"/>, clamped to 0.1 through 1.</param>
    /// <param name="directory">The cache directory. The default is <c>basin-audio</c> under <c>XDG_RUNTIME_DIR</c>, else under the temporary directory.</param>
    /// <returns>The path of the WAV file in <paramref name="directory"/>, named by the quantized parameters and a generator version.</returns>
    /// <exception cref="IOException">The method cannot create the directory or write the file.</exception>
    public static string PathFor(float rt60Seconds, float preDelaySeconds = 0.012f, float crossoverHz = 3000, float highDecayRatio = 0.5f, string? directory = null)
    {
        var rt60 = MathF.Max(SecondsQuantum, MathF.Round(rt60Seconds / SecondsQuantum) * SecondsQuantum);
        var preDelay = MathF.Round(Math.Clamp(preDelaySeconds, 0, 0.2f) * 1000) / 1000;
        var crossover = MathF.Round(Math.Clamp(crossoverHz, 200, 16000));
        var ratio = MathF.Round(Math.Clamp(highDecayRatio, 0.1f, 1), 2);
        directory ??= DefaultDirectory();
        var name = string.Create(CultureInfo.InvariantCulture, $"room-v1-rt{rt60:F2}-pd{preDelay:F3}-x{crossover:F0}-h{ratio:F2}.wav");
        var path = Path.Combine(directory, name);
        if (File.Exists(path))
        {
            return path;
        }

        Directory.CreateDirectory(directory);
        var (left, right) = Generate(rt60, preDelay, crossover, ratio);
        var temporary = path + $".{Environment.ProcessId}.tmp";
        Write(temporary, left, right);
        File.Move(temporary, path, overwrite: true);
        return path;
    }

    /// <summary>
    /// Generates a response without quantizing or caching it. The result is decorrelated noise per channel, normalized to unit energy, with a faster decay above the crossover.
    /// </summary>
    /// <param name="rt60Seconds">The time in seconds for the low band to decay by 60 dB.</param>
    /// <param name="preDelaySeconds">The silence before the response in seconds.</param>
    /// <param name="crossoverHz">The frequency in hertz that splits the low and high bands.</param>
    /// <param name="highDecayRatio">The high-band reverb time as a fraction of <paramref name="rt60Seconds"/>.</param>
    /// <returns>The left and right channels at <see cref="Rate"/>. The same arguments always give the same samples.</returns>
    public static (float[] Left, float[] Right) Generate(float rt60Seconds, float preDelaySeconds, float crossoverHz, float highDecayRatio)
    {
        var length = (int)((preDelaySeconds + (rt60Seconds * 1.1f)) * Rate) + 1;
        var delay = (int)(preDelaySeconds * Rate);
        var left = new float[length];
        var right = new float[length];
        var seed = BitConverter.SingleToInt32Bits(rt60Seconds) ^ (BitConverter.SingleToInt32Bits(preDelaySeconds) * 31) ^
            (BitConverter.SingleToInt32Bits(crossoverHz) * 17) ^ (BitConverter.SingleToInt32Bits(highDecayRatio) * 7);
        Fill(left, delay, rt60Seconds, crossoverHz, highDecayRatio, new Random(seed));
        Fill(right, delay, rt60Seconds, crossoverHz, highDecayRatio, new Random(seed ^ 0x5bd1e995));
        Normalize(left, right);
        return (left, right);
    }

    private static void Fill(float[] samples, int delay, float rt60, float crossover, float highRatio, Random random)
    {
        var lowDecay = MathF.Log(1000) / (rt60 * Rate);
        var highDecay = MathF.Log(1000) / (rt60 * highRatio * Rate);
        var alpha = 1 - MathF.Exp(-2 * MathF.PI * crossover / Rate);
        float a = 0, b = 0, c = 0, low = 0;
        for (var i = delay; i < samples.Length; i++)
        {
            var noise = ((float)random.NextDouble() * 2) - 1;
            a += alpha * (noise - a);
            b += alpha * (a - b);
            c += alpha * (b - c);
            low += alpha * (c - low);
            var high = noise - low;
            var t = i - delay;
            samples[i] = (low * MathF.Exp(-lowDecay * t)) + (high * MathF.Exp(-highDecay * t));
        }
    }

    private static void Normalize(float[] left, float[] right)
    {
        double energy = 0;
        for (var i = 0; i < left.Length; i++)
        {
            energy += (left[i] * (double)left[i]) + (right[i] * (double)right[i]);
        }

        var scale = energy > 0 ? (float)(1 / Math.Sqrt(energy / 2)) : 0;
        for (var i = 0; i < left.Length; i++)
        {
            left[i] *= scale;
            right[i] *= scale;
        }
    }

    private static void Write(string path, float[] left, float[] right)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + (left.Length * 8));
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)3);
        writer.Write((short)2);
        writer.Write(Rate);
        writer.Write(Rate * 8);
        writer.Write((short)8);
        writer.Write((short)32);
        writer.Write("data"u8);
        writer.Write(left.Length * 8);
        for (var i = 0; i < left.Length; i++)
        {
            writer.Write(left[i]);
            writer.Write(right[i]);
        }
    }

    private static string DefaultDirectory()
    {
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        return Path.Combine(runtime is { Length: > 0 } ? runtime : Path.GetTempPath(), "basin-audio");
    }
}
