using System.Buffers.Binary;

namespace Basin.Audio.PipeWire;

/// <summary>
/// A mono sound at <see cref="Rate"/> that a <see cref="SoundPlayer"/> plays. The player reads the samples and never changes them.
/// </summary>
public sealed class SoundClip
{
    /// <summary>
    /// The sample rate in hertz of every clip and of the player stream.
    /// </summary>
    public const int Rate = 48000;

    /// <summary>
    /// Initializes a new instance of the <see cref="SoundClip"/> class. The clip keeps the array and does not copy it.
    /// </summary>
    /// <param name="name">The name for error messages.</param>
    /// <param name="samples">The mono samples at <see cref="Rate"/>, nominally from -1 to 1.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="samples"/> is <see langword="null"/>.</exception>
    public SoundClip(string name, float[] samples)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(samples);
        Name = name;
        Samples = samples;
    }

    /// <summary>
    /// Gets the name that the clip was created with.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the mono samples at <see cref="Rate"/>. The array is shared, so a change shows in every later playback.
    /// </summary>
    public float[] Samples { get; }

    /// <summary>
    /// Gets the length of the clip at its original speed.
    /// </summary>
    public TimeSpan Duration => TimeSpan.FromSeconds(Samples.Length / (double)Rate);

    /// <summary>
    /// Reads a WAV file into a mono clip at <see cref="Rate"/>. It accepts 8-bit unsigned PCM, 16-bit PCM and 32-bit float. It averages the channels and resamples another rate linearly.
    /// </summary>
    /// <param name="name">The name of the clip, also used in error messages.</param>
    /// <param name="stream">The WAV data. The method reads it to the end and does not dispose it.</param>
    /// <returns>A new clip with the decoded samples.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The data is not a RIFF WAVE file, has no data chunk, or has a sample format that the method does not read.</exception>
    public static SoundClip FromWav(string name, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        var bytes = copy.GetBuffer().AsSpan(0, (int)copy.Length);
        if (bytes.Length < 12 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes[8..12].SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException($"{name} is not a WAV file");
        }

        int format = 0, channels = 0, rate = 0, bits = 0;
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var id = bytes.Slice(offset, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(bytes[(offset + 4)..]);
            var body = offset + 8;
            if (id.SequenceEqual("fmt "u8))
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(bytes[body..]);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(body + 2)..]);
                rate = BinaryPrimitives.ReadInt32LittleEndian(bytes[(body + 4)..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(body + 14)..]);
                if (format == 0xFFFE && size >= 40)
                {
                    format = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(body + 24)..]);
                }
            }
            else if (id.SequenceEqual("data"u8))
            {
                var length = Math.Min(Math.Max(0, size), bytes.Length - body);
                return new SoundClip(name, Decode(name, bytes.Slice(body, length), format, channels, rate, bits));
            }

            offset = body + Math.Max(0, size) + (size & 1);
        }

        throw new InvalidDataException($"{name} has no data chunk");
    }

    private static float[] Decode(string name, ReadOnlySpan<byte> data, int format, int channels, int rate, int bits)
    {
        if (channels <= 0 || rate <= 0 || !((format == 1 && bits is 8 or 16) || (format == 3 && bits == 32)))
        {
            throw new InvalidDataException($"{name} is not 8-bit or 16-bit PCM or 32-bit float");
        }

        var width = bits / 8;
        var frames = data.Length / (width * channels);
        var mono = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            var sum = 0f;
            for (var c = 0; c < channels; c++)
            {
                var at = data[(((i * channels) + c) * width)..];
                sum += format == 3 ? BinaryPrimitives.ReadSingleLittleEndian(at)
                    : bits == 8 ? (at[0] - 128) / 128f
                    : BinaryPrimitives.ReadInt16LittleEndian(at) / 32768f;
            }

            mono[i] = sum / channels;
        }

        if (rate == Rate)
        {
            return mono;
        }

        var resampled = new float[(int)((long)frames * Rate / rate)];
        for (var i = 0; i < resampled.Length; i++)
        {
            var at = (double)i * rate / Rate;
            var index = (int)at;
            var next = Math.Min(index + 1, frames - 1);
            var t = (float)(at - index);
            resampled[i] = (mono[index] * (1 - t)) + (mono[next] * t);
        }

        return resampled;
    }
}
