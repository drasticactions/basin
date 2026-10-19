using System.Buffers.Binary;

namespace Basin.Audio.PipeWire;

public sealed class SoundClip
{
    public const int Rate = 48000;

    public SoundClip(string name, float[] samples)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(samples);
        Name = name;
        Samples = samples;
    }

    public string Name { get; }

    public float[] Samples { get; }

    public TimeSpan Duration => TimeSpan.FromSeconds(Samples.Length / (double)Rate);

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
