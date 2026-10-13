using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Basin.Transport.Waypipe;

namespace Basin.Tests;

internal static class WaypipeCorpus
{
    public static bool Recording => Environment.GetEnvironmentVariable("BASIN_RECORD_CORPUS") == "1";

    public static string SourceDirectory([CallerFilePath] string sourcePath = "") =>
        Path.Combine(Path.GetDirectoryName(sourcePath)!, "Corpus", "waypipe");

    public static string OutputDirectory => Path.Combine(AppContext.BaseDirectory, "Corpus", "waypipe");

    public static IEnumerable<string> Names() =>
        Directory.Exists(OutputDirectory)
            ? Directory.GetFiles(OutputDirectory, "*.bin").Select(Path.GetFileName).Order(StringComparer.Ordinal)!
            : [];

    public static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(OutputDirectory, name));

    public static void Write(string name, ReadOnlySpan<byte> stream, int budget = int.MaxValue)
    {
        var bytes = stream.ToArray();
        var keep = Trim(bytes, budget);
        Directory.CreateDirectory(SourceDirectory());
        File.WriteAllBytes(Path.Combine(SourceDirectory(), name), bytes.AsSpan(0, keep).ToArray());
    }

    public static bool CarriesDmabuf(byte[] stream) => stream.Length >= 4 && (stream[0] & 0x4) == 0;

    public static WaypipeCompression CompressionOf(byte[] stream) =>
        (WaypipeCompression)((BinaryPrimitives.ReadUInt32LittleEndian(stream) >> 8) & 0x7);

    public static List<int> FrameOffsets(byte[] stream)
    {
        var offsets = new List<int>();
        var offset = WaypipeWire.ConnectionHeaderLength;
        while (offset + 4 <= stream.Length)
        {
            var (length, _) = WaypipeWire.ParseHeader(BinaryPrimitives.ReadUInt32LittleEndian(stream.AsSpan(offset)));
            if (length < 4 || offset + WaypipeWire.Padded(length) > stream.Length)
            {
                break;
            }

            offsets.Add(offset);
            offset += WaypipeWire.Padded(length);
        }

        return offsets;
    }

    private static int Trim(byte[] stream, int budget)
    {
        var end = WaypipeWire.ConnectionHeaderLength;
        foreach (var offset in FrameOffsets(stream))
        {
            var (length, _) = WaypipeWire.ParseHeader(BinaryPrimitives.ReadUInt32LittleEndian(stream.AsSpan(offset)));
            var next = offset + WaypipeWire.Padded(length);
            if (next > budget)
            {
                break;
            }

            end = next;
        }

        return end;
    }

    public static byte[] Mutate(byte[] original, List<int> frames, Random random, WaypipeLimits limits, out string what)
    {
        var bytes = (byte[])original.Clone();
        var frame = frames.Count == 0 ? WaypipeWire.ConnectionHeaderLength : frames[random.Next(frames.Count)];
        var header = frame + 4 <= bytes.Length ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(frame)) : 0u;
        var (length, type) = WaypipeWire.ParseHeader(header);
        switch (random.Next(10))
        {
            case 0:
            {
                var flips = 1 + random.Next(8);
                for (var i = 0; i < flips; i++)
                {
                    bytes[random.Next(bytes.Length)] ^= (byte)(1 << random.Next(8));
                }

                what = $"{flips} bit flips";
                return bytes;
            }

            case 1:
            {
                var lengths = new[] { 0, 1, 3, 4, length - 1, length + 1, length + 4, (1 << 27) - 1 };
                var value = lengths[random.Next(lengths.Length)];
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(frame), WaypipeWire.Header(type, Math.Max(0, value)));
                what = $"the {type} frame at {frame} declares {value} bytes rather than {length}";
                return bytes;
            }

            case 2:
            {
                var cut = random.Next(bytes.Length);
                what = $"truncated at {cut}";
                return bytes.AsSpan(0, cut).ToArray();
            }

            case 3:
            {
                var other = frames.Count == 0 ? frame : frames[random.Next(frames.Count)];
                var (otherLength, otherType) = WaypipeWire.ParseHeader(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(other)));
                var piece = bytes.AsSpan(other, WaypipeWire.Padded(otherLength)).ToArray();
                var at = random.Next(2) == 0 ? frame : WaypipeWire.ConnectionHeaderLength + (random.Next(bytes.Length - WaypipeWire.ConnectionHeaderLength) & ~3);
                what = $"the {otherType} frame at {other} spliced in at {at}";
                return [.. bytes.AsSpan(0, at), .. piece, .. bytes.AsSpan(at)];
            }

            case 4:
            {
                var protocol = frames.Where(f => (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(f)) & 0x1f) == 0
                    && WaypipeWire.ParseHeader(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(f))).Length >= 12).ToList();
                if (protocol.Count == 0)
                {
                    goto case 0;
                }

                var target = protocol[random.Next(protocol.Count)];
                var header2 = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(target + 8));
                var fds = random.Next(32);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(target + 8), WaypipeWire.TagFdCount(WaypipeWire.StripFdTag(header2), fds));
                what = $"the protocol frame at {target} tags {fds} fds";
                return bytes;
            }

            case 5:
            {
                if (length < 8)
                {
                    goto case 0;
                }

                var ids = new[] { limits.MaxRemoteIds - 1, limits.MaxRemoteIds, limits.MaxRemoteIds + 1, -1, 0, int.MaxValue, int.MinValue };
                var id = ids[random.Next(ids.Length)];
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frame + 4), id);
                what = $"the {type} frame at {frame} names remote id {id}";
                return bytes;
            }

            case 6:
            {
                var opens = new List<byte>();
                var count = limits.MaxRemoteIds + 1 + random.Next(4);
                Span<byte> open = stackalloc byte[12];
                for (var i = 0; i < count; i++)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(open, WaypipeWire.Header(WaypipeMessageType.OpenFile, 12));
                    BinaryPrimitives.WriteInt32LittleEndian(open[4..], 100_000 + i);
                    BinaryPrimitives.WriteInt32LittleEndian(open[8..], 4096);
                    opens.AddRange(open);
                }

                what = $"{count} files opened before the frame at {frame}";
                return [.. bytes.AsSpan(0, frame), .. opens, .. bytes.AsSpan(frame)];
            }

            case 7:
            {
                if (length < 8)
                {
                    goto case 0;
                }

                var field = frame + 4 + (random.Next((length - 4) / 4) * 4);
                var values = new[] { 0, -1, 1, int.MaxValue, int.MinValue, limits.MaxRegionBytes, limits.MaxRegionBytes + 1, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(field)) + 1 };
                var value = values[random.Next(values.Length)];
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(field), value);
                what = $"the {type} frame at {frame} carries {value} at {field}";
                return bytes;
            }

            case 8:
            {
                var piece = bytes.AsSpan(frame, Math.Min(WaypipeWire.Padded(length), bytes.Length - frame)).ToArray();
                what = $"the {type} frame at {frame} sent twice";
                return [.. bytes.AsSpan(0, frame), .. piece, .. bytes.AsSpan(frame)];
            }

            default:
            {
                var types = Enum.GetValues<WaypipeMessageType>();
                var swapped = types[random.Next(types.Length)];
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(frame), WaypipeWire.Header(swapped, length));
                what = $"the {type} frame at {frame} relabeled {swapped}";
                return bytes;
            }
        }
    }
}
